# Card Batch Intake API

An ASP.NET Core Web API that lets bank clients submit batches of card records, validates every batch against seven business rules, stores valid batches as **Accepted**, and lets an operator **release** an accepted batch to production.

A full card number (unmasked PAN) is treated as a security incident: the whole batch is rejected, and the number is never stored, logged or echoed back.
---

## Contents

- [1. Understanding the project](#1-understanding-the-project)
- [2. Design decisions](#2-design-decisions)
- [3. How I built it](#3-how-i-built-it)
- [4. How to run it](#4-how-to-run-it)
- [5. Error responses](#5-error-responses)
- [6. Assumptions](#6-assumptions)
- [7. Tests](#7-tests)
- [8. AI tool use](#8-ai-tool-use)
- [9. What I would improve with more time](#9-what-i-would-improve-with-more-time)

---

## 1. Understanding the project

### The problem

Banks issue cards to their customers. Before cards can be produced, a bank has to send the details of each card: whose name goes on it, which card product it is, the last four digits of the card number (masked), and when it expires. Banks send these details in groups called **batches**, one batch per file.

If a batch has mistakes (an unknown product, an expired date, the same record twice), the wrong cards get made. Worse, if a bank accidentally sends a **full card number**, sensitive data has leaked into a system that must never hold it.

### What this API does

This API is the front door for those batches. It:

- **receives** batches from bank clients,
- **validates** every batch against seven rules and reports **every** problem in one response,
- **rejects** any batch that contains a full card number, without ever storing, logging or echoing that number,
- **stores** valid batches with the status **Accepted** and the time they arrived (UTC),
- lets people **look up** one batch, or a filtered and paged list,
- lets an operator **release** an Accepted batch to production, changing its status to **Released**.

### The life of a batch

A batch starts when a bank client submits it to the API. The API checks it against every rule straight away.

If anything is wrong, the batch is turned away with a `422` response that lists every problem found, and nothing is saved. Because it was never saved, a rejected batch has no status at all. The client fixes the problems and submits it again. If the batch contains a full card number, it is turned away with a security error instead, and the number is never kept anywhere.

If the batch passes every rule, the API checks whether a batch with the same file name has already been submitted. If it has, the new one is turned away with a `409` response, because the same file must never be processed twice.

If the batch is valid and its file name is new, it is saved with the status **Accepted**, together with the time it arrived, in UTC. The client gets a `201` response containing the batch and its new id, which it can use to look the batch up later.

An Accepted batch waits until an operator decides to send it to production. When the operator releases it, its status changes to **Released** and the release time is recorded.

Releasing is final and can only happen once. A batch can never go back from Released to Accepted, and trying to release a batch that is already Released gives a `409` response.

### Endpoints

| Method | Route | Purpose | Success | Failures |
|---|---|---|---|---|
| `POST` | `/api/batches` | Submit a new batch | `201 Created` with the batch, its `id` and a `Location` header | `400` malformed JSON · `422` validation errors · `409` file name already submitted |
| `GET` | `/api/batches/{id}` | Get one batch | `200 OK` | `404` not found |
| `GET` | `/api/batches` | List batches. Optional filters `clientCode` and `status`; paging with `page` (default 1) and `pageSize` (default 20, max 100) | `200 OK` | `400` if `status` is not `Accepted` or `Released` |
| `POST` | `/api/batches/{id}/release` | Release an accepted batch to production | `200 OK`, status becomes `Released` | `404` not found · `409` batch is not `Accepted` |

#### Example request

`POST /api/batches`

```json
{
  "clientCode": "KAYA",
  "fileName": "KAYA_20261013_001.txt",
  "expectedRecordCount": 2,
  "records": [
    { "recordId": "R0001", "cardholderName": "T MOKOENA", "maskedPan": "************4821", "productCode": "DEBIT_GOLD", "expiry": "09/29" },
    { "recordId": "R0002", "cardholderName": "J VAN WYK", "maskedPan": "************1937", "productCode": "DEBIT_CLASSIC", "expiry": "11/28" }
  ]
}
```

#### Example response: `201 Created`

```json
{
  "id": "ec5e8146-0705-4e50-9c0e-8f3147b80faa",
  "clientCode": "KAYA",
  "fileName": "KAYA_20261013_001.txt",
  "expectedRecordCount": 2,
  "status": "Accepted",
  "receivedAtUtc": "2026-10-16T11:36:02.1593758Z",
  "releasedAtUtc": null,
  "records": [
    { "recordId": "R0001", "cardholderName": "T MOKOENA", "maskedPan": "************4821", "productCode": "DEBIT_GOLD", "expiry": "09/29" },
    { "recordId": "R0002", "cardholderName": "J VAN WYK", "maskedPan": "************1937", "productCode": "DEBIT_CLASSIC", "expiry": "11/28" }
  ]
}
```

#### List response shape

`GET /api/batches?clientCode=KAYA&status=Accepted&page=1&pageSize=20`

```json
{
  "items": [ /* batches, newest first */ ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1
}
```

### Validation rules

Every rule is checked, and **every** error found is returned in a single `422` response. Each error names the field it relates to and, for record-level errors, the record.

| # | Rule | Error code(s) |
|---|---|---|
| 1 | `clientCode` is 3 to 6 uppercase letters | `INVALID_CLIENT_CODE` |
| 2 | `fileName` follows `{clientCode}_{yyyyMMdd}_{seq}.txt`, the date is a real date, and the client code matches `clientCode` | `INVALID_FILE_NAME`, `INVALID_FILE_DATE`, `FILE_NAME_CLIENT_MISMATCH` |
| 3 | `expectedRecordCount` equals the number of records sent | `RECORD_COUNT_MISMATCH` |
| 4 | `recordId` is unique within the batch (every record sharing a duplicate id is flagged) | `DUPLICATE_RECORD_ID` |
| 5 | `maskedPan` is exactly 12 asterisks followed by 4 digits | `INVALID_MASKED_PAN` |
| 5 | A full, unmasked card number anywhere rejects the whole batch with a security error | `UNMASKED_PAN_DETECTED` |
| 6 | `expiry` is `MM/YY` and not in the past | `INVALID_EXPIRY_FORMAT`, `EXPIRY_IN_PAST` |
| 7 | `productCode` is `DEBIT_CLASSIC`, `DEBIT_GOLD` or `CREDIT_PLAT` | `INVALID_PRODUCT_CODE` |
| - | A required field is missing or blank | `REQUIRED` |

A valid batch is saved with status `Accepted` and `receivedAtUtc` set to the current UTC time. An invalid batch is not saved.

### Project structure

The solution has two projects: the API itself, called **CardBatchIntakeAll**, and its tests, called **CardBatchIntakeAll.Tests**. The API project is split into folders, and each folder has one job.

**Models** describes the data the API stores. `Batch` is one whole batch: its id, client code, file name, expected record count, status, received and released times, and its list of cards. `CardRecord` is one card inside a batch. It only ever holds the masked card number. `BatchStatus` lists the only two states a batch can be in: Accepted or Released.

**Data** connects the models to the database. `AppDbContext` tells Entity Framework Core that there are two tables, Batches and CardRecords, and how they relate: one batch has many cards. It also makes sure no two batches can have the same file name, and stores the status as readable text.

**Dtos** describes what travels in and out of the API. `CreateBatchRequest` is what a client sends when submitting a batch. It contains only the fields a client is allowed to send, and every field may be empty, so missing fields can be reported clearly. `BatchResponse` is what the API sends back for a batch, and `PagedResponse` wraps one page of a list with the page number, page size and total count.

**Validation** holds the business rules. `BatchValidator` checks a submitted batch for an unmasked card number first, then against all seven rules, and collects every problem it finds. `ValidationError` describes one problem, and `ValidationResult` holds all of them together.

**Controllers** is where requests arrive. `BatchesController` has the four endpoints. For each request it asks the validator, talks to the database, and chooses the right response code.

At the top of the API project, **Program.cs** starts the application. It registers the database, the validator and the clock, creates the database file on first run, and turns on Swagger. **appsettings.json** holds the settings: the database file name and how much to log.

The **CardBatchIntakeAll.Tests** project contains `BatchValidatorTests`, which tests every validation rule with cases that should pass and cases that should fail.

At the root of the repository, next to the solution file, are the files for running the API in Docker. The **Dockerfile** holds the instructions for building a container image of the API. **.dockerignore** lists the files Docker should leave out, such as build output, Git history and the local database. **DOCKER_REQUIREMENTS.md** explains what you need installed to use Docker, and lists every project file with whether it is copied into the image.

---

## 2. Design decisions

**Controllers.** All four endpoints sit together in `BatchesController` under `/api/batches`. `[ApiController]` gives an automatic `400` for malformed JSON. The controller is deliberately thin: it receives the request, asks the validator, talks to the database and chooses the status code.

**Persistence: EF Core with SQLite.** The brief requires `dotnet run` to work with no other setup. SQLite is a single file with nothing to install, and unlike an in-memory store the data survives a restart, so the API behaves like a real system. The database is created on startup with `EnsureCreated()`. The trade-off is that SQLite is not built for heavy concurrent traffic, and `EnsureCreated` cannot evolve an existing schema (see improvements).

**The validator is its own class.** All business rules live in `BatchValidator`, which has no web or database code. That keeps the controller simple and lets every rule be unit-tested directly, without starting a server.

**Collect every error.** The validator starts with an empty list and runs every check, adding each problem, rather than returning at the first failure. Each error carries a `field`, a machine-readable `code`, a readable `message` and, where relevant, the `recordId`.

**Security check first.** Before any other rule, the validator looks for an unmasked card number (13 to 19 digits, ignoring spaces and dashes). If it finds one, it returns **only** the security error, so no other detail from that batch is echoed back. A `recordId` is only echoed if it looks harmless, in case a card number was hidden inside it.

**DTOs separate from models.** `CreateBatchRequest` contains only what a client may send, so a client cannot set `id`, `status` or timestamps. Every request field is nullable, so a missing field produces a clear `422 REQUIRED` error instead of a vague framework `400` (or a missing number silently becoming `0`). `BatchResponse` controls exactly what is returned.

**Duplicate file names.** The controller checks for an existing file name and returns a friendly `409`. A **unique index on `FileName`** in the database is the real guard: if two identical requests arrive at the same moment and both pass the check, the database rejects the second, which is also mapped to `409`.

**Time is injected.** The validator and controller get the current time from `TimeProvider`, not `DateTime.Now`. The app registers the system clock; the tests use a `FakeTimeProvider` frozen at 15 October 2026, so the expiry tests never go stale. All timestamps are UTC, and the UTC marker is restored when reading from SQLite so responses end in `Z`.

**Guid ids.** Batch ids are GUIDs: they can't be guessed or enumerated, don't reveal how many batches exist, and are created by the app before saving. This is not a substitute for authentication.

**No private data in logs.** The code logs only batch ids, client codes, record counts and error counts. Request bodies, cardholder names and card numbers are never logged. Framework logging for ASP.NET Core and EF Core is set to `Warning`, so request details and SQL are not written either.

**Paging.** `pageSize` defaults to 20 and is capped at 100. Out-of-range `page` or `pageSize` values are corrected rather than rejected. Results are ordered newest first.

**Docker.** The Dockerfile uses a two-stage build: the .NET SDK image compiles the API, and only the compiled output is copied into the much smaller ASP.NET runtime image, so no source code or compiler ships in the final image. The container runs as a non-root user, and the database lives in its own `/app/data` folder so it can be kept in a Docker volume. `.dockerignore` keeps the local `cardbatches.db` (which holds test cardholder names) out of the image.

---

## 3. How I built it

I built the API in **Visual Studio**, from the bottom up, so that each layer only depended on layers that already existed. I committed after each step.

**1. Created the project.**
I created a new **ASP.NET Core Web API** project called `CardBatchIntakeAll`, ran it once to check the template worked, and created the Git repository. I then deleted the template's `WeatherForecast` sample and installed the NuGet packages `Microsoft.EntityFrameworkCore.Sqlite` and `Microsoft.EntityFrameworkCore.Design`.

**2. Models: the shape of the data.**
In a `Models` folder I created `BatchStatus` (Accepted or Released), `CardRecord` (one card) and `Batch` (the whole batch with its list of cards). Everything else builds on these, so they came first.

**3. Data: the database.**
In a `Data` folder I created `AppDbContext`, which turns the models into the `Batches` and `CardRecords` tables, adds a unique index on `FileName` and stores the status as text. I added the connection string `Data Source=cardbatches.db` to `appsettings.json`, and registered the context in `Program.cs` with `EnsureCreated()` on startup.

**4. Ran the app: the database was created.**
When I ran the app, EF Core created the **`cardbatches.db`** SQLite file with both tables automatically. This proved the database layer worked before any endpoints existed, and it is why the API needs no database setup after cloning.

**5. DTOs: what comes in and goes out.**
In a `Dtos` folder I created `CreateBatchRequest` (what a client sends, with every field nullable), `BatchResponse` (what the API returns, with a `FromEntity` mapping method) and `PagedResponse` (one page of a list).

**6. Validation: the business rules.**
In a `Validation` folder I created `ValidationError`/`ValidationResult` and `BatchValidator`, with the unmasked-PAN security check and all seven rules. I registered the validator and `TimeProvider.System` (the clock) in `Program.cs`.

**7. Controllers: the endpoints.**
Last, in the `Controllers` folder I created `BatchesController` with the four endpoints. It ties everything together: it receives a DTO, asks the validator, saves through `AppDbContext`, and returns the right status code. I also turned EF Core logging down to `Warning` in `appsettings.json`.

**8. Tested manually in Swagger.**
At `https://localhost:7158/swagger` I checked every endpoint: `201` for a valid batch, `409` for the same file name again, `422` with several errors listed together, `422` with the security error for a full card number, `200` and `404` for getting a batch, `200` for the filtered list, and `200` then `409` for releasing the same batch twice.

**9. Added a test project to the same solution.**
I added an **xUnit Test Project** called `CardBatchIntakeAll.Tests` to the same solution (**File > Add > New Project**), gave it a **project reference** to `CardBatchIntakeAll`, and installed `Microsoft.Extensions.TimeProvider.Testing` for the fake clock. I wrote `BatchValidatorTests` with passing and failing cases for every rule, and ran them in Test Explorer: **45 passed, 0 failed**.

**10. Added Docker support.**
As an optional extra, I added a `Dockerfile` and `.dockerignore` at the root of the repository, next to the solution file, so the API can run in a container without .NET installed. I also wrote `DOCKER_REQUIREMENTS.md`, which lists what's needed to run it and which project files go into the image.

**11. Wrote this README.**

---

## 4. How to run it

### Requirements

- **Docker Desktop**, installed and running. You don't need .NET installed.
- Nothing else. The database is a SQLite file that the app creates automatically on first run.

[DOCKER_REQUIREMENTS.md](DOCKER_REQUIREMENTS.md) lists everything Docker needs and exactly which project files are copied into the image.

### Run the API with Docker

From the repository root (the folder with the `Dockerfile`):

```bash
git clone <repo-url>
cd <repo-folder>
docker build -t cardbatchintake .
docker run --rm -p 8080:8080 -e ASPNETCORE_ENVIRONMENT=Development -v cardbatch-data:/app/data cardbatchintake
```

Then open **`http://localhost:8080/swagger`**. Swagger lists all four endpoints and lets you send requests from the browser (**Try it out**, then **Execute**). Stop the API with **Ctrl+C**.

- `-p 8080:8080` connects port 8080 on your computer to the API inside the container.
- `-e ASPNETCORE_ENVIRONMENT=Development` turns Swagger on. Swagger is only enabled in Development.
- `-v cardbatch-data:/app/data` keeps the SQLite database in a Docker volume, so saved batches survive when the container stops. Leave it out to start fresh every time.

### Run the API without Docker

With the .NET SDK (`net10.0`) installed, from the repository root:

```bash
dotnet run --project CardBatchIntakeAll
```

The console prints the address it's listening on (`Now listening on: http://localhost:...`). Add `/swagger` to it.

### Run the tests

The tests run with the .NET SDK, from the repository root:

```bash
dotnet test
```

Expected result: **45 passed, 0 failed**.

### Start with a clean database

- **Docker:** stop the container and run `docker volume rm cardbatch-data`.
- **Without Docker:** stop the app and delete `cardbatches.db` (and `cardbatches.db-shm` / `cardbatches.db-wal` if present).

A fresh, empty database is created on the next run.

---

## 5. Error responses

### `422` validation errors

```json
{
  "title": "Validation failed.",
  "status": 422,
  "securityViolation": false,
  "errors": [
    { "field": "clientCode", "code": "INVALID_CLIENT_CODE", "message": "clientCode must be 3 to 6 uppercase letters.", "recordId": null },
    { "field": "records[1].expiry", "code": "EXPIRY_IN_PAST", "message": "expiry is in the past.", "recordId": "R0002" }
  ]
}
```

### `422` security error (unmasked card number)

Only the security error is returned, and the card number itself never appears in the response.

```json
{
  "title": "Security error: batch contains an unmasked card number.",
  "status": 422,
  "securityViolation": true,
  "errors": [
    { "field": "records[0].maskedPan", "code": "UNMASKED_PAN_DETECTED", "message": "Record R0001 contains an unmasked card number. The whole batch was rejected.", "recordId": "R0001" }
  ]
}
```

### `409` and `404`

```json
{ "title": "File name already submitted.", "status": 409, "detail": "A batch with this fileName has already been submitted." }
{ "title": "Batch is not Accepted.", "status": 409, "detail": "Only Accepted batches can be released. Current status: Released." }
{ "title": "Batch not found.", "status": 404, "detail": "No batch exists with id ..." }
```

---

## 6. Assumptions

- **File name separators.** The brief's pattern `{clientCode}{yyyyMMdd}{seq}.txt` shows no separators, but its example `KAYA_20261013_001.txt` uses underscores. I followed the example: `CODE_yyyyMMdd_seq.txt`.
- **The date in the file name must be a real calendar date** (`20261340` is rejected).
- **Expiry.** A card is valid until the **end** of its expiry month, so `10/26` is still valid during October 2026. `YY` means `20YY`. "Today" is evaluated in UTC.
- **Unmasked PAN.** Any `maskedPan` that is 13 to 19 digits once spaces and dashes are removed is treated as a full card number. When this happens only the security error is returned, not the other validation errors.
- **Duplicate file names are checked across all clients**, not per client (the file name already contains the client code).
- **Order of checks.** Validation runs before the duplicate check, so a batch that is both invalid and a duplicate gets `422`, not `409`.
- **`cardholderName` is required.** It isn't one of the seven rules, but a card record without a name isn't usable.
- **Exact matching.** Client codes, product codes and the `clientCode` list filter are case-sensitive (`debit_gold` is not a valid product code).
- **A batch can be released once.** Releasing a `Released` batch returns `409`.

---

## 7. Tests

The `CardBatchIntakeAll.Tests` project uses **xUnit** and tests `BatchValidator` directly.

- Every one of the 7 rules has passing and failing cases, including boundaries (client codes of exactly 3 and 6 letters, a card expiring in the current month).
- Additional tests prove that several errors are returned together, that an unmasked card number is caught plain, with spaces and with dashes, and that the number never appears in any error message or field.
- Each test starts from a valid batch and breaks one thing, so a failing test points straight at its cause.
- `[Theory]` with `[InlineData]` runs one method against several values: 21 test methods produce **45 tests**.
- A `FakeTimeProvider` freezes the clock at 15 October 2026.

The endpoints and status codes (`201`, `409`, `422`, the security error, `200`/`404`, release `200` then `409`) were verified manually in Swagger.

---

## 8. AI tool use

I used **Claude** (Anthropic's AI assistant) as a pair-programmer and tutor throughout this task:

- **Planning.** I talked through the brief, its ambiguities and the overall design before writing code.
- **Code.** Claude assist in generating and verifying some of the code for the  DTOs, validator, controller and unit tests
- **Verification.** I tested every endpoint manually in Swagger and ran the test suite until all 45 tests passed.
- **Understanding.** I asked Claude to explain every line, folder and design decision, and worked through those explanations so I can explain and change the code myself.
- **Troubleshooting.** Claude helped with setup problems, such as a malformed `appsettings.json`, adding the test project to the solution, and Swagger input issues.

I'm responsible for this submission and happy to walk through or change any part of it.

---

## 9. What I would improve with more time

**Security and access**
- Authentication and roles: only operators may release a batch, and a client may only see its own batches.
- An audit trail recording who released each batch and when.
- Rate limiting and a maximum request size / maximum records per batch.

**Correctness**
- Reject empty batches. A batch with zero records and `expectedRecordCount: 0` currently passes validation.
- Optimistic concurrency (a row version) on release, so two simultaneous release requests can't both succeed.
- A Luhn check as an extra signal for unmasked-PAN detection.

**Data**
- EF Core migrations instead of `EnsureCreated()`, so the schema can evolve safely.
- A production database (for example SQL Server or PostgreSQL) instead of SQLite.
- A lighter summary DTO for the list endpoint, without the card records.

**Testing**
- Integration tests with `WebApplicationFactory` and an isolated database, covering every status code automatically.
- A test that captures log output during a submission and proves no cardholder name or card number is written.

**API polish**
- `[ProducesResponseType]` attributes so Swagger documents every response code.
- `application/problem+json` (`ProblemDetails`) for all error responses.

**Finish the remaining optional extras**

With more time I would finish these two optional extras from the brief:

- **A simple Razor Page that lists batches.** A read-only page at `/Batches` showing each batch's client code, file name, status, record count and received and released times, newest first, with filters for client code and status and 20 batches per page. It would deliberately show no cardholder names or card numbers, because an operator only needs to see which batches exist and where they are.
- **A health check endpoint.** A `/health` endpoint using ASP.NET Core's built-in health checks, with a database check, so it reports `Healthy` only when the app is running **and** can reach the database, and `503 Unhealthy` otherwise. Monitoring tools and container platforms could call it to know whether the service is working.
