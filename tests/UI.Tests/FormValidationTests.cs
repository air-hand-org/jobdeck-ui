using System.ComponentModel.DataAnnotations;
using ui.Models;

namespace UI.Tests;

public sealed class FormValidationTests
{
    [Theory]
    [InlineData("method")]
    [InlineData("timeout")]
    [InlineData("attempts")]
    [InlineData("delay")]
    [InlineData("codes")]
    [InlineData("url")]
    [InlineData("name")]
    public void UntrustedFormValuesAreRejected(string field)
    {
        var form = new HttpJobFormModel { Name = "example", Url = "https://example.com", Method = JobHttpMethod.Get };
        switch (field)
        {
            case "method": form.Method = (JobHttpMethod)999; break;
            case "timeout": form.TimeoutSeconds = long.MaxValue; break;
            case "attempts": form.MaxAttempts = 0; break;
            case "delay": form.InitialDelaySeconds = 10; form.MaxDelaySeconds = 1; break;
            case "codes": form.ExpectedStatusCodes = null!; break;
            case "url": form.Url = "ftp://example.com"; break;
            case "name": form.Name = new string('a', 71); break;
        }
        Assert.False(Validator.TryValidateObject(form, new ValidationContext(form), [], true));
    }
}
