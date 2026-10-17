using CardBatchIntakeAll.Models;

namespace CardBatchIntakeAll.Dtos
{

      // Batch data returned by the API
    public class BatchResponse
    {
        public Guid Id { get; set; }
        public string ClientCode { get; set; } = "";
        public string FileName { get; set; } = "";
        public int ExpectedRecordCount { get; set; }
        public string Status { get; set; } = "";
        public DateTime ReceivedAtUtc { get; set; }
        public DateTime? ReleasedAtUtc { get; set; }
        public List<CardRecordResponse> Records { get; set; } = new();
   
        // Map entity to DTO
        public static BatchResponse FromEntity(Batch batch)
        {
            return new BatchResponse
            {
                Id = batch.Id,
                ClientCode = batch.ClientCode,
                FileName = batch.FileName,
                ExpectedRecordCount = batch.ExpectedRecordCount,
                Status = batch.Status.ToString(),

                 // Mark dates as UTC so JSON includes "Z"
                ReceivedAtUtc = DateTime.SpecifyKind(batch.ReceivedAtUtc, DateTimeKind.Utc),
                ReleasedAtUtc = batch.ReleasedAtUtc.HasValue
                ? DateTime.SpecifyKind(batch.ReleasedAtUtc.Value, DateTimeKind.Utc)
                   : null,

                      // Map each card record
                Records = batch.Records
                    .Select(r => new CardRecordResponse
                    {
                        RecordId = r.RecordId,
                        CardholderName = r.CardholderName,
                        MaskedPan = r.MaskedPan,
                        ProductCode = r.ProductCode,
                        Expiry = r.Expiry
                    })
                    .ToList()
            };
        }
    }

   // Card record data returned by the API (no internal IDs)
    public class CardRecordResponse
    {
        public string RecordId { get; set; } = "";
        public string CardholderName { get; set; } = "";
        public string MaskedPan { get; set; } = "";
        public string ProductCode { get; set; } = "";
        public string Expiry { get; set; } = "";
    }
}