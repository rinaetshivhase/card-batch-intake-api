
using System.Globalization;
using System.Text.RegularExpressions;
using CardBatchIntakeAll.Dtos;

namespace CardBatchIntakeAll.Validation
{

    // Single validation failure
    public class BatchValidator
    {


        // Format rules
        private static readonly Regex ClientCodeRegex = new(@"^[A-Z]{3,6}$", RegexOptions.Compiled);
        private static readonly Regex FileNameRegex = new(@"^([A-Z]{3,6})_([0-9]{8})_([0-9]{3})\.txt$", RegexOptions.Compiled);
        private static readonly Regex MaskedPanRegex = new(@"^\*{12}[0-9]{4}$", RegexOptions.Compiled);
        private static readonly Regex FullPanRegex = new(@"^[0-9]{13,19}$", RegexOptions.Compiled);
        private static readonly Regex ExpiryRegex = new(@"^(0[1-9]|1[0-2])/([0-9]{2})$", RegexOptions.Compiled);
        private static readonly Regex SafeRecordIdRegex = new(@"^[A-Za-z0-9_-]{1,20}$", RegexOptions.Compiled);

  
        // Valid product codes
        private static readonly HashSet<string> AllowedProductCodes = new()
        {
            "DEBIT_CLASSIC", "DEBIT_GOLD", "CREDIT_PLAT"
        };

        private readonly TimeProvider _clock;

        public BatchValidator(TimeProvider clock)
        {
            _clock = clock;
        }
     // Runs all checks and returns the errors found
        public ValidationResult Validate(CreateBatchRequest request)
        {
            var result = new ValidationResult();

            if (CheckForUnmaskedPans(request, result))
            {
                result.IsSecurityViolation = true;
                return result;
            }

            ValidateClientCode(request, result);
            ValidateFileName(request, result);
            ValidateRecordCount(request, result);
            ValidateRecords(request, result);

            return result;
        }
     // Flags any record containing a full card number
        private static bool CheckForUnmaskedPans(CreateBatchRequest request, ValidationResult result)
        {
            if (request.Records == null) return false;

            for (int i = 0; i < request.Records.Count; i++)
            {
                var record = request.Records[i];
                if (record?.MaskedPan == null) continue;

                var digitsOnly = record.MaskedPan.Replace(" ", "").Replace("-", "");
                if (FullPanRegex.IsMatch(digitsOnly))
                {
                    var safeId = SafeRecordId(record.RecordId);
                    result.Errors.Add(new ValidationError
                    {
                        Field = $"records[{i}].maskedPan",
                        Code = "UNMASKED_PAN_DETECTED",
                        Message = safeId != null
                            ? $"Record {safeId} contains an unmasked card number. The whole batch was rejected."
                            : $"Record at position {i} contains an unmasked card number. The whole batch was rejected.",
                        RecordId = safeId
                    });
                }
            }

            return result.Errors.Count > 0;
        }

        private static void ValidateClientCode(CreateBatchRequest request, ValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(request.ClientCode))
            {
                Add(result, "clientCode", "REQUIRED", "clientCode is required.");
            }
            else if (!ClientCodeRegex.IsMatch(request.ClientCode))
            {
                Add(result, "clientCode", "INVALID_CLIENT_CODE", "clientCode must be 3 to 6 uppercase letters.");
            }
        }

        private static void ValidateFileName(CreateBatchRequest request, ValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(request.FileName))
            {
                Add(result, "fileName", "REQUIRED", "fileName is required.");
                return;
            }

            var match = FileNameRegex.Match(request.FileName);
            if (!match.Success)
            {
                Add(result, "fileName", "INVALID_FILE_NAME",
                    "fileName must follow {clientCode}_{yyyyMMdd}_{seq}.txt, e.g. KAYA_20261013_001.txt.");
                return;
            }

            if (!DateTime.TryParseExact(match.Groups[2].Value, "yyyyMMdd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                Add(result, "fileName", "INVALID_FILE_DATE", "The date in fileName is not a real date.");
            }

            if (request.ClientCode != null && match.Groups[1].Value != request.ClientCode)
            {
                Add(result, "fileName", "FILE_NAME_CLIENT_MISMATCH", "The client code in fileName does not match clientCode.");
            }
        }

        private static void ValidateRecordCount(CreateBatchRequest request, ValidationResult result)
        {
            if (request.ExpectedRecordCount == null)
            {
                Add(result, "expectedRecordCount", "REQUIRED", "expectedRecordCount is required.");
            }

            if (request.Records == null)
            {
                Add(result, "records", "REQUIRED", "records is required.");
                return;
            }

            if (request.ExpectedRecordCount != null && request.ExpectedRecordCount != request.Records.Count)
            {
                Add(result, "expectedRecordCount", "RECORD_COUNT_MISMATCH",
                    $"expectedRecordCount is {request.ExpectedRecordCount} but {request.Records.Count} records were sent.");
            }
        }

        private void ValidateRecords(CreateBatchRequest request, ValidationResult result)
        {
            if (request.Records == null) return;

            var duplicateIds = request.Records
                .Where(r => !string.IsNullOrWhiteSpace(r?.RecordId))
                .GroupBy(r => r!.RecordId!)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet();

            for (int i = 0; i < request.Records.Count; i++)
            {
                var record = request.Records[i];
                var prefix = $"records[{i}]";

                if (record == null)
                {
                    Add(result, prefix, "REQUIRED", $"Record at position {i} is empty.");
                    continue;
                }

                var safeId = SafeRecordId(record.RecordId);

                if (string.IsNullOrWhiteSpace(record.RecordId))
                {
                    Add(result, $"{prefix}.recordId", "REQUIRED", "recordId is required.");
                }
                else if (duplicateIds.Contains(record.RecordId))
                {
                    Add(result, $"{prefix}.recordId", "DUPLICATE_RECORD_ID",
                        "recordId must be unique within the batch.", safeId);
                }

                if (string.IsNullOrWhiteSpace(record.CardholderName))
                {
                    Add(result, $"{prefix}.cardholderName", "REQUIRED", "cardholderName is required.", safeId);
                }

                if (string.IsNullOrWhiteSpace(record.MaskedPan))
                {
                    Add(result, $"{prefix}.maskedPan", "REQUIRED", "maskedPan is required.", safeId);
                }
                else if (!MaskedPanRegex.IsMatch(record.MaskedPan))
                {
                    Add(result, $"{prefix}.maskedPan", "INVALID_MASKED_PAN",
                        "maskedPan must be 12 asterisks followed by 4 digits.", safeId);
                }

                ValidateExpiry(record.Expiry, prefix, safeId, result);

                if (string.IsNullOrWhiteSpace(record.ProductCode))
                {
                    Add(result, $"{prefix}.productCode", "REQUIRED", "productCode is required.", safeId);
                }
                else if (!AllowedProductCodes.Contains(record.ProductCode))
                {
                    Add(result, $"{prefix}.productCode", "INVALID_PRODUCT_CODE",
                        "productCode must be DEBIT_CLASSIC, DEBIT_GOLD or CREDIT_PLAT.", safeId);
                }
            }
        }

        private void ValidateExpiry(string? expiry, string prefix, string? safeId, ValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(expiry))
            {
                Add(result, $"{prefix}.expiry", "REQUIRED", "expiry is required.", safeId);
                return;
            }

            var match = ExpiryRegex.Match(expiry);
            if (!match.Success)
            {
                Add(result, $"{prefix}.expiry", "INVALID_EXPIRY_FORMAT", "expiry must be in MM/YY format.", safeId);
                return;
            }

            int month = int.Parse(match.Groups[1].Value);
            int year = 2000 + int.Parse(match.Groups[2].Value);
            var firstDayAfterExpiry = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);

            if (_clock.GetUtcNow().UtcDateTime >= firstDayAfterExpiry)
            {
                Add(result, $"{prefix}.expiry", "EXPIRY_IN_PAST", "expiry is in the past.", safeId);
            }
        }

        private static string? SafeRecordId(string? recordId)
        {
            if (recordId == null || !SafeRecordIdRegex.IsMatch(recordId)) return null;
            if (recordId.Count(char.IsDigit) >= 13) return null;
            return recordId;
        }

        private static void Add(ValidationResult result, string field, string code, string message, string? recordId = null)
        {
            result.Errors.Add(new ValidationError
            {
                Field = field,
                Code = code,
                Message = message,
                RecordId = recordId
            });
        }
    }
}