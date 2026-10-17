using CardBatchIntakeAll.Data;
using CardBatchIntakeAll.Dtos;
using CardBatchIntakeAll.Models;
using CardBatchIntakeAll.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CardBatchIntakeAll.Controllers
{

     // API endpoints for submitting, viewing and releasing batches
    [ApiController]
    [Route("api/batches")]
    public class BatchesController : ControllerBase
    {
        private const int MaxPageSize = 100;

        private readonly AppDbContext _db;
        private readonly BatchValidator _validator;
        private readonly TimeProvider _clock;
        private readonly ILogger<BatchesController> _logger;

        public BatchesController(
            AppDbContext db,
            BatchValidator validator,
            TimeProvider clock,
            ILogger<BatchesController> logger)
        {
            _db = db;
            _validator = validator;
            _clock = clock;
            _logger = logger;
        }


    // POST api/batches - submit a new batch
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateBatchRequest request)
        {
            var result = _validator.Validate(request);

            if (!result.IsValid)
            {
                if (result.IsSecurityViolation)
                {
                    _logger.LogWarning("Batch rejected: unmasked card number detected in {Count} record(s)",
                        result.Errors.Count);
                }
                else
                {
                    _logger.LogInformation("Batch rejected with {Count} validation error(s)", result.Errors.Count);
                }

                return StatusCode(StatusCodes.Status422UnprocessableEntity, new
                {
                    title = result.IsSecurityViolation
                        ? "Security error: batch contains an unmasked card number."
                        : "Validation failed.",
                    status = 422,
                    securityViolation = result.IsSecurityViolation,
                    errors = result.Errors
                });
            }
       // Reject duplicate file names early
            if (await _db.Batches.AnyAsync(b => b.FileName == request.FileName))
            {
                return FileNameConflict();
            }
     // Map request to entity (values are safe to use after validation)
            var batch = new Batch
            {
                ClientCode = request.ClientCode!,
                FileName = request.FileName!,
                ExpectedRecordCount = request.ExpectedRecordCount!.Value,
                Status = BatchStatus.Accepted,
                ReceivedAtUtc = _clock.GetUtcNow().UtcDateTime,
                Records = request.Records!.Select(r => new CardRecord
                {
                    RecordId = r.RecordId!,
                    CardholderName = r.CardholderName!,
                    MaskedPan = r.MaskedPan!,
                    ProductCode = r.ProductCode!,
                    Expiry = r.Expiry!
                }).ToList()
            };

            _db.Batches.Add(batch);

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                return FileNameConflict();
            }

            _logger.LogInformation("Batch {BatchId} accepted for client {ClientCode} with {RecordCount} records",
                batch.Id, batch.ClientCode, batch.Records.Count);

            return CreatedAtAction(nameof(GetById), new { id = batch.Id }, BatchResponse.FromEntity(batch));
        }

            // GET api/batches/{id} - get one batch with its records

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var batch = await _db.Batches
                .AsNoTracking()
                .Include(b => b.Records)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (batch == null)
            {
                return BatchNotFound(id);
            }

            return Ok(BatchResponse.FromEntity(batch));
        }

        // GET api/batches - list batches with optional filters and paging
        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] string? clientCode,
            [FromQuery] BatchStatus? status,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > MaxPageSize) pageSize = MaxPageSize;

            var query = _db.Batches.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(clientCode))
            {
                query = query.Where(b => b.ClientCode == clientCode);
            }

            if (status != null)
            {
                query = query.Where(b => b.Status == status);
            }

            var totalCount = await query.CountAsync();

            var batches = await query
                .OrderByDescending(b => b.ReceivedAtUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Include(b => b.Records)
                .ToListAsync();

            return Ok(new PagedResponse<BatchResponse>
            {
                Items = batches.Select(BatchResponse.FromEntity).ToList(),
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount
            });
        }

        // POST api/batches/{id}/release - mark an accepted batch as released
        [HttpPost("{id:guid}/release")]
        public async Task<IActionResult> Release(Guid id)
        {
            var batch = await _db.Batches
                .Include(b => b.Records)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (batch == null)
            {
                return BatchNotFound(id);
            }

            if (batch.Status != BatchStatus.Accepted)
            {
                return Conflict(new
                {
                    title = "Batch is not Accepted.",
                    status = 409,
                    detail = $"Only Accepted batches can be released. Current status: {batch.Status}."
                });
            }

            batch.Status = BatchStatus.Released;
            batch.ReleasedAtUtc = _clock.GetUtcNow().UtcDateTime;
            await _db.SaveChangesAsync();

            _logger.LogInformation("Batch {BatchId} released for client {ClientCode}", batch.Id, batch.ClientCode);

            return Ok(BatchResponse.FromEntity(batch));
        }

        private IActionResult BatchNotFound(Guid id)
        {
            return NotFound(new
            {
                title = "Batch not found.",
                status = 404,
                detail = $"No batch exists with id {id}."
            });
        }

        private IActionResult FileNameConflict()
        {
            return Conflict(new
            {
                title = "File name already submitted.",
                status = 409,
                detail = "A batch with this fileName has already been submitted."
            });
        }
    }
}
