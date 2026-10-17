namespace CardBatchIntakeAll.Validation
{
    // Single validation failure
    public class ValidationError
    {
        public string Field { get; set; } = "";
        public string Code { get; set; } = "";
        public string Message { get; set; } = "";
        public string? RecordId { get; set; }
    }

     // Outcome of validating a request
    public class ValidationResult
    {
        public List<ValidationError> Errors { get; } = new();
        public bool IsSecurityViolation { get; set; }
        public bool IsValid => Errors.Count == 0;
    }
}