using System.ComponentModel.DataAnnotations;
using ui.Validation;

using JobdeckHttpMethod = ui.Models.JobHttpMethod;

namespace ui.Models;

public sealed class HttpJobFormModel : IValidatableObject
{
    [Required]
    [StringLength(70)]
    public string? Name { get; set; }

    [Required]
    [Url]
    public string? Url { get; set; }

    [NotUnspecifiedEnum(ErrorMessage = "HTTP method is required.")]
    public JobdeckHttpMethod Method { get; set; } = JobdeckHttpMethod.Unspecified;

    [Required]
    public List<int> ExpectedStatusCodes { get; set; } = [200];

    [Required]
    [Range(1, 315576000000L, ErrorMessage = "Timeout must be at least 1 second.")]
    public long TimeoutSeconds { get; set; } = 1;

    [Required]
    public bool FollowRedirects { get; set; }

    [Required]
    public bool RetryOnTimeout { get; set; }

    [Required]
    public bool RetryOnNetworkError { get; set; }

    [Required]
    public List<int> RetryOnStatusCodes { get; set; } = [];

    [Required]
    [Range(1, int.MaxValue)]
    public int MaxAttempts { get; set; } = 1;

    [Required]
    [Range(0, 315576000000L)]
    public long InitialDelaySeconds { get; set; } = 0;

    [Required]
    [Range(0, 315576000000L)]
    public long MaxDelaySeconds { get; set; } = 0;

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            yield return new ValidationResult("An HTTP or HTTPS URL is required.", [nameof(Url)]);
        }
        if (InitialDelaySeconds > MaxDelaySeconds)
        {
            yield return new ValidationResult("Initial delay must not exceed max delay.", [nameof(InitialDelaySeconds)]);
        }
        if (ExpectedStatusCodes is null || ExpectedStatusCodes.Count == 0)
        {
            yield return new ValidationResult("Expected status codes is required.", [nameof(ExpectedStatusCodes)]);
        }

        if (ExpectedStatusCodes?.Any(code => code is < 100 or > 599) == true)
        {
            yield return new ValidationResult("Expected status codes must be between 100 and 599.", [nameof(ExpectedStatusCodes)]);
        }

        if (RetryOnStatusCodes?.Any(code => code is < 100 or > 599) == true)
        {
            yield return new ValidationResult("Retry status codes must be between 100 and 599.", [nameof(RetryOnStatusCodes)]);
        }
    }

}
