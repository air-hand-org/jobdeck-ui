namespace ui.Models;

// Data-only browser contracts. No backend RPC clients or owner credentials.
public enum JobHttpMethod { Unspecified, Get, Head, Post, Put, Patch, Delete }
public sealed record JobView(string Id, string Name, bool Enabled, HttpJobFormModel Form);
public sealed record ExecutionView(string Id, int Attempt, string Status, DateTime? CreatedAt, DateTime? UpdatedAt, string Result);
