namespace Tsabo.Data.Schema;

public class ValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
