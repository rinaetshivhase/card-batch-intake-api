using CardBatchIntakeAll.Dtos;
using CardBatchIntakeAll.Validation;
using Microsoft.Extensions.Time.Testing;

namespace CardBatchIntakeAll.Tests
{
    public class BatchValidatorTests
    {
        private readonly BatchValidator _validator =
            new(new FakeTimeProvider(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero)));

        private static CreateBatchRequest ValidRequest() => new()
        {
            ClientCode = "KAYA",
            FileName = "KAYA_20261013_001.txt",
            ExpectedRecordCount = 2,
            Records = new List<CardRecordRequest>
            {
                new() { RecordId = "R0001", CardholderName = "T MOKOENA", MaskedPan = "************4821", ProductCode = "DEBIT_GOLD", Expiry = "09/29" },
                new() { RecordId = "R0002", CardholderName = "J VAN WYK", MaskedPan = "************1937", ProductCode = "DEBIT_CLASSIC", Expiry = "11/28" }
            }
        };

        private static bool HasError(ValidationResult result, string code) =>
            result.Errors.Any(e => e.Code == code);

        [Fact]
        public void ValidBatch_HasNoErrors()
        {
            var result = _validator.Validate(ValidRequest());

            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void InvalidBatch_ReturnsEveryError_NotJustTheFirst()
        {
            var request = ValidRequest();
            request.ClientCode = "kaya";
            request.ExpectedRecordCount = 5;
            request.Records![0].ProductCode = "GOLD";
            request.Records[1].Expiry = "01/20";

            var result = _validator.Validate(request);

            Assert.True(HasError(result, "INVALID_CLIENT_CODE"));
            Assert.True(HasError(result, "RECORD_COUNT_MISMATCH"));
            Assert.True(HasError(result, "INVALID_PRODUCT_CODE"));
            Assert.True(HasError(result, "EXPIRY_IN_PAST"));
        }

        // Rule 1: clientCode is 3 to 6 uppercase letters

        [Theory]
        [InlineData("ABC")]
        [InlineData("KAYA")]
        [InlineData("ABCDEF")]
        public void Rule1_ValidClientCode_Passes(string clientCode)
        {
            var request = ValidRequest();
            request.ClientCode = clientCode;

            var result = _validator.Validate(request);

            Assert.False(HasError(result, "INVALID_CLIENT_CODE"));
        }

        [Theory]
        [InlineData("kaya")]
        [InlineData("AB")]
        [InlineData("ABCDEFG")]
        [InlineData("KA1A")]
        public void Rule1_InvalidClientCode_Fails(string clientCode)
        {
            var request = ValidRequest();
            request.ClientCode = clientCode;

            var result = _validator.Validate(request);

            Assert.True(HasError(result, "INVALID_CLIENT_CODE"));
            Assert.Contains(result.Errors, e => e.Field == "clientCode");
        }

        // Rule 2: fileName follows {clientCode}_{yyyyMMdd}_{seq}.txt

        [Fact]
        public void Rule2_ValidFileName_Passes()
        {
            var result = _validator.Validate(ValidRequest());

            Assert.DoesNotContain(result.Errors, e => e.Field == "fileName");
        }

        [Theory]
        [InlineData("KAYA20261013001.txt")]
        [InlineData("KAYA_20261013_01.txt")]
        [InlineData("KAYA_20261013_001.csv")]
        [InlineData("KAYA_2026101_001.txt")]
        public void Rule2_WrongPattern_Fails(string fileName)
        {
            var request = ValidRequest();
            request.FileName = fileName;

            var result = _validator.Validate(request);

            Assert.True(HasError(result, "INVALID_FILE_NAME"));
        }

        [Fact]
        public void Rule2_ClientCodeMismatch_Fails()
        {
            var request = ValidRequest();
            request.FileName = "ABSA_20261013_001.txt";

            var result = _validator.Validate(request);

            Assert.True(HasError(result, "FILE_NAME_CLIENT_MISMATCH"));
        }

        [Fact]
        public void Rule2_ImpossibleDate_Fails()
        {
            var request = ValidRequest();
            request.FileName = "KAYA_20261340_001.txt";

            var result = _validator.Validate(request);

            Assert.True(HasError(result, "INVALID_FILE_DATE"));
        }

        // Rule 3: expectedRecordCount equals the number of records

        [Fact]
        public void Rule3_MatchingCount_Passes()
        {
            var result = _validator.Validate(ValidRequest());

            Assert.False(HasError(result, "RECORD_COUNT_MISMATCH"));
        }

        [Fact]
        public void Rule3_MismatchedCount_Fails()
        {
            var request = ValidRequest();
            request.ExpectedRecordCount = 3;

            var result = _validator.Validate(request);

            Assert.True(HasError(result, "RECORD_COUNT_MISMATCH"));
        }

        // Rule 4: recordId is unique within the batch

        [Fact]
        public void Rule4_UniqueRecordIds_Pass()
        {
            var result = _validator.Validate(ValidRequest());

            Assert.False(HasError(result, "DUPLICATE_RECORD_ID"));
        }

        [Fact]
        public void Rule4_DuplicateRecordIds_Fail_AndBothRecordsAreFlagged()
        {
            var request = ValidRequest();
            request.Records![1].RecordId = "R0001";

            var result = _validator.Validate(request);

            Assert.Equal(2, result.Errors.Count(e => e.Code == "DUPLICATE_RECORD_ID"));
            Assert.All(result.Errors.Where(e => e.Code == "DUPLICATE_RECORD_ID"),
                e => Assert.Equal("R0001", e.RecordId));
        }

        // Rule 5: maskedPan is 12 asterisks + 4 digits; a full card number rejects the batch

        [Fact]
        public void Rule5_ValidMaskedPan_Passes()
        {
            var result = _validator.Validate(ValidRequest());

            Assert.False(HasError(result, "INVALID_MASKED_PAN"));
            Assert.False(result.IsSecurityViolation);
        }

        [Theory]
        [InlineData("***********4821")]
        [InlineData("*************4821")]
        [InlineData("************48A1")]
        [InlineData("1234")]
        public void Rule5_WrongMaskFormat_Fails(string maskedPan)
        {
            var request = ValidRequest();
            request.Records![0].MaskedPan = maskedPan;

            var result = _validator.Validate(request);

            Assert.True(HasError(result, "INVALID_MASKED_PAN"));
            Assert.Contains(result.Errors, e => e.RecordId == "R0001");
        }

        [Theory]
        [InlineData("4111111111111111")]
        [InlineData("4111 1111 1111 1111")]
        [InlineData("4111-1111-1111-1111")]
        public void Rule5_FullCardNumber_RejectsBatchWithSecurityError(string fullPan)
        {
            var request = ValidRequest();
            request.Records![1].MaskedPan = fullPan;

            var result = _validator.Validate(request);

            Assert.True(result.IsSecurityViolation);
            Assert.All(result.Errors, e => Assert.Equal("UNMASKED_PAN_DETECTED", e.Code));
            Assert.Equal("R0002", result.Errors.Single().RecordId);
        }

        [Fact]
        public void Rule5_FullCardNumber_IsNeverEchoedInErrors()
        {
            var request = ValidRequest();
            request.Records![0].MaskedPan = "4111111111111111";

            var result = _validator.Validate(request);

            Assert.All(result.Errors, e =>
            {
                Assert.DoesNotContain("4111111111111111", e.Message);
                Assert.DoesNotContain("4111111111111111", e.Field);
            });
        }

        // Rule 6: expiry is MM/YY and not in the past (clock frozen at 15 Oct 2026)

        [Theory]
        [InlineData("10/26")]
        [InlineData("11/26")]
        [InlineData("01/30")]
        public void Rule6_CurrentOrFutureExpiry_Passes(string expiry)
        {
            var request = ValidRequest();
            request.Records![0].Expiry = expiry;

            var result = _validator.Validate(request);

            Assert.False(HasError(result, "EXPIRY_IN_PAST"));
            Assert.False(HasError(result, "INVALID_EXPIRY_FORMAT"));
        }

        [Theory]
        [InlineData("09/26")]
        [InlineData("12/25")]
        public void Rule6_PastExpiry_Fails(string expiry)
        {
            var request = ValidRequest();
            request.Records![0].Expiry = expiry;

            var result = _validator.Validate(request);

            Assert.True(HasError(result, "EXPIRY_IN_PAST"));
        }

        [Theory]
        [InlineData("9/29")]
        [InlineData("13/29")]
        [InlineData("00/29")]
        [InlineData("09-29")]
        [InlineData("0929")]
        public void Rule6_WrongFormat_Fails(string expiry)
        {
            var request = ValidRequest();
            request.Records![0].Expiry = expiry;

            var result = _validator.Validate(request);

            Assert.True(HasError(result, "INVALID_EXPIRY_FORMAT"));
        }

        // Rule 7: productCode is DEBIT_CLASSIC, DEBIT_GOLD or CREDIT_PLAT

        [Theory]
        [InlineData("DEBIT_CLASSIC")]
        [InlineData("DEBIT_GOLD")]
        [InlineData("CREDIT_PLAT")]
        public void Rule7_AllowedProductCode_Passes(string productCode)
        {
            var request = ValidRequest();
            request.Records![0].ProductCode = productCode;

            var result = _validator.Validate(request);

            Assert.False(HasError(result, "INVALID_PRODUCT_CODE"));
        }

        [Theory]
        [InlineData("DEBIT_PLATINUM")]
        [InlineData("debit_gold")]
        [InlineData("GOLD")]
        public void Rule7_UnknownProductCode_Fails(string productCode)
        {
            var request = ValidRequest();
            request.Records![0].ProductCode = productCode;

            var result = _validator.Validate(request);

            Assert.True(HasError(result, "INVALID_PRODUCT_CODE"));
        }
    }
}