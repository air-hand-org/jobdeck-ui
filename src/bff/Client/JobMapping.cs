using Google.Protobuf.WellKnownTypes;
using Jobdeck.Job.V1;
using ui.Models;
using JStatusCodes = Jobdeck.Job.V1.StatusCodes;

namespace ui.Client;

public static class JobMapping
{
    public static JobSpec ToJobSpec(this HttpJobFormModel model)
    {
        return new JobSpec
        {
            Type = JobType.HttpRequest,
            Http = new HttpJobConfig
            {
                Url = model.Url!,
                Method = (Jobdeck.Job.V1.HttpMethod)model.Method,
                ExpectedStatusCodes = new JStatusCodes
                {
                    Codes = { model.ExpectedStatusCodes }
                },
                Timeout = Duration.FromTimeSpan(TimeSpan.FromSeconds(model.TimeoutSeconds)),
                FollowRedirects = model.FollowRedirects,
                RetryOnTimeout = model.RetryOnTimeout,
                RetryOnNetworkError = model.RetryOnNetworkError,
                RetryOnStatusCodes = new JStatusCodes
                {
                    Codes = { model.RetryOnStatusCodes },
                },
            },
            RetryPolicy = new RetryPolicy
            {
                MaxAttempts = model.MaxAttempts,
                BackoffType = BackoffType.Fixed,
                InitialDelay = Duration.FromTimeSpan(TimeSpan.FromSeconds(model.InitialDelaySeconds)),
                MaxDelay = Duration.FromTimeSpan(TimeSpan.FromSeconds(model.MaxDelaySeconds)),
            }
        };
    }

    public static HttpJobFormModel FromProto(Job job)
    {
        if (job.Spec?.Http is null)
        {
            throw new ArgumentException("Job must be an HTTP job.", nameof(job));
        }

        var http = job.Spec.Http;
        var retryPolicy = job.Spec.RetryPolicy ?? new RetryPolicy();

        return new HttpJobFormModel
        {
            Name = job.Name,
            Url = http.Url,
            Method = (JobHttpMethod)http.Method,
            ExpectedStatusCodes = http.ExpectedStatusCodes?.Codes.ToList() ?? [],
            TimeoutSeconds = (long)(http.Timeout?.ToTimeSpan().TotalSeconds ?? 0),
            FollowRedirects = http.FollowRedirects,
            RetryOnTimeout = http.RetryOnTimeout,
            RetryOnNetworkError = http.RetryOnNetworkError,
            RetryOnStatusCodes = http.RetryOnStatusCodes?.Codes.ToList() ?? [],
            MaxAttempts = retryPolicy.MaxAttempts,
            InitialDelaySeconds = (long)(retryPolicy.InitialDelay?.ToTimeSpan().TotalSeconds ?? 0),
            MaxDelaySeconds = (long)(retryPolicy.MaxDelay?.ToTimeSpan().TotalSeconds ?? 0),
        };
    }
}
