namespace CardBatchIntakeAll.Models;
// Client batch file, tracked from receipt to release
public class Batch
{
    public Guid Id { get; set; }
    public string ClientCode { get; set; } = "";
    public string FileName { get; set; } = "";
    public int ExpectedRecordCount { get; set; }
    public BatchStatus Status { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public DateTime? ReleasedAtUtc { get; set; }

    public List<CardRecord> Records { get; set; } = new();
}