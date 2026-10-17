namespace CardBatchIntakeAll.Models;

// Single card in a batch
public class CardRecord
{
    public int Id { get; set; }
    public string RecordId { get; set; } = "";
    public string CardholderName { get; set; } = "";
    public string MaskedPan { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string Expiry { get; set; } = "";

    public Guid BatchId { get; set; }
}
