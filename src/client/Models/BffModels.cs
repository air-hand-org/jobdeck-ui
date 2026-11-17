using Google.Protobuf;
using Jobdeck.Job.V1;

namespace ui.Models;

// Data-only browser contracts. No backend RPC clients or owner credentials.
public enum JobHttpMethod { Unspecified, Get, Head, Post, Put, Patch, Delete }
public sealed record JobView(string Id, string Name, bool Enabled, HttpJobFormModel Form)
{
    public JobView(Job job) : this(
        job.Id
        , job.Name
        , job.Enabled
        , JobMapping.FromProto(job)
    )
    {}
}

public sealed record ExecutionView(string Id, int Attempt, string Status, DateTime? CreatedAt, DateTime? UpdatedAt, string Result)
{
    public ExecutionView(Execution execution) : this(
        execution.Id
        , execution.Attempt
        , execution.Status.ToString()
        , execution.CreatedAt.ToDateTime()
        , execution.UpdatedAt.ToDateTime()
        , execution.Result is null ? "-" : JsonFormatter.Default.Format(execution.Result)
    )
    {}
}
