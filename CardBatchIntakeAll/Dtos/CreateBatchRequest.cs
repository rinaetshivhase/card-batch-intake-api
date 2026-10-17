namespace CardBatchIntakeAll.Dtos
{

     // Request body for creating a batch
    // Fields are nullable so missing values can be caught in validation
    public class CreateBatchRequest
    {
        public string? ClientCode { get; set; }
        public string? FileName { get; set; }
        public int? ExpectedRecordCount { get; set; }
        public List<CardRecordRequest>? Records { get; set; }
    }

    public class CardRecordRequest
    {
        public string? RecordId { get; set; }
        public string? CardholderName { get; set; }
        public string? MaskedPan { get; set; }
        public string? ProductCode { get; set; }
        public string? Expiry { get; set; }
    }
}
