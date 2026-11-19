using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ui.Client.Api;
using ui.Client.Components.Jobs;
using ui.Client.Components.Pages.JobPages;
using ui.Client.Models;

namespace UI.Tests;

// Exercise the compiled Razor component and real EditForm events without a browser.
#pragma warning disable BL0006 // RenderTree APIs are used only by the test renderer.
public sealed class HttpJobFormTests
{
    [Fact]
    public async Task SuccessfulSubmitEnablesAnotherUpdateOnTheSameForm()
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new FormRenderer(services);
        var calls = 0;
        await renderer.ShowAsync(() => { calls++; return Task.CompletedTask; });
        Assert.False(renderer.SubmitDisabled);

        await renderer.SubmitAsync();

        Assert.Equal(1, calls);
        Assert.False(renderer.SubmitDisabled);
        await renderer.SubmitAsync();
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task InFlightSubmitRejectsOverlappingEventsWithoutRetrying()
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new FormRenderer(services);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        await renderer.ShowAsync(async () =>
        {
            calls++;
            started.TrySetResult();
            await finish.Task;
        });

        var submit = renderer.SubmitAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(renderer.SubmitDisabled);
            await renderer.SubmitAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(1, calls);
        }
        finally
        {
            finish.TrySetResult();
            await submit.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        Assert.False(renderer.SubmitDisabled);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task HandledFailureEnablesExplicitResubmitWithoutAutomaticRetry()
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new FormRenderer(services);
        var calls = 0;
        await renderer.ShowAsync(async () =>
        {
            calls++;
            await renderer.SetErrorAsync("Update failed; check the result before submitting again.");
        });

        await renderer.SubmitAsync();

        Assert.Equal(1, calls);
        Assert.False(renderer.SubmitDisabled);
        Assert.Contains(renderer.RenderedFrames, frame => frame.FrameType == RenderTreeFrameType.Text &&
            frame.TextContent == "Update failed; check the result before submitting again.");
        await renderer.SubmitAsync();
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ThrownFailureReleasesSubmitGuardAndPreservesTheException()
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new FormRenderer(services);
        var error = new InvalidOperationException("update failed");
        var calls = 0;
        await renderer.ShowAsync(() => ++calls == 1 ? Task.FromException(error) : Task.CompletedTask);

        await renderer.SubmitAsync();

        Assert.Equal(1, calls);
        Assert.Same(error, Assert.Single(renderer.Exceptions));
        // An unhandled event exception goes to the error boundary rather than completing a render.
        await renderer.SetErrorAsync("Check the result before submitting again.");
        Assert.False(renderer.SubmitDisabled);
        await renderer.SubmitAsync();
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task EditRefreshDoesNotNeedToReplaceTheFormToAllowAnotherUpdate()
    {
        var client = new EditClient();
        var navigation = new RefreshNavigation();
        using var services = new ServiceCollection().AddLogging()
            .AddSingleton<IJobPageClient>(client)
            .AddSingleton<NavigationManager>(navigation).BuildServiceProvider();
        await using var renderer = new FormRenderer(services);
        await renderer.ShowEditAsync();
        var form = Assert.Single(renderer.RenderedFrames, frame => frame.FrameType == RenderTreeFrameType.Component && frame.Component is HttpJobForm).Component;
        Assert.False(renderer.SubmitDisabled);

        await renderer.SubmitAsync();

        Assert.Equal(1, client.UpdateCalls);
        Assert.Equal(1, navigation.RefreshCalls);
        Assert.Same(form, Assert.Single(renderer.RenderedFrames, frame => frame.FrameType == RenderTreeFrameType.Component && frame.Component is HttpJobForm).Component);
        Assert.False(renderer.SubmitDisabled);
        await renderer.SubmitAsync();
        Assert.Equal(2, client.UpdateCalls);
        Assert.Equal(2, navigation.RefreshCalls);
        Assert.Empty(renderer.Exceptions);
    }

    private sealed class RefreshNavigation : NavigationManager
    {
        public int RefreshCalls { get; private set; }
        public RefreshNavigation() => Initialize("https://example.com/", "https://example.com/jobs/job-id/edit");
        public override void Refresh(bool forceReload = false) => RefreshCalls++;
    }

    private sealed class EditClient : IJobPageClient
    {
        private readonly JobView _job = new("job-id", "example", true, new HttpJobFormModel
        {
            Name = "example",
            Url = "https://example.com",
            Method = JobHttpMethod.Get
        });
        public int UpdateCalls { get; private set; }
        public Task<ApiResult<JobView>> GetJobAsync(string jobId, CancellationToken cancellationToken = default) => Task.FromResult(ApiResult<JobView>.Success(_job));
        public Task<ApiResult<JobView>> UpdateJobAsync(string jobId, HttpJobFormModel model, CancellationToken cancellationToken = default)
        {
            Assert.Equal("job-id", jobId);
            UpdateCalls++;
            return Task.FromResult(ApiResult<JobView>.Success(_job));
        }
        public Task<ApiResult<ListJobsResult>> ListJobsAsync(int pageSize, string pageToken = "", CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResult<ListExecutionsResult>> ListExecutionsAsync(string jobId, int pageSize, string pageToken = "", CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResult<JobView>> CreateJobAsync(HttpJobFormModel model, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResult> DeleteJobAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResult> DoExecutionAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FormRenderer(IServiceProvider services) : Renderer(services, NullLoggerFactory.Instance)
    {
        private int _root;
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

        public Task ShowEditAsync() => Dispatcher.InvokeAsync(async () =>
        {
            _root = AssignRootComponentId(InstantiateComponent(typeof(Edit)));
            await RenderRootComponentAsync(_root, ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(Edit.JobId)] = "job-id"
            }));
        });

        public Task ShowAsync(Func<Task> submit) => Dispatcher.InvokeAsync(async () =>
        {
            _root = AssignRootComponentId(new HttpJobForm());
            await RenderRootComponentAsync(_root, ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(HttpJobForm.Model)] = new HttpJobFormModel
                {
                    Name = "example",
                    Url = "https://example.com",
                    Method = JobHttpMethod.Get
                },
                [nameof(HttpJobForm.FormName)] = "UpdateJob",
                [nameof(HttpJobForm.SubmitText)] = "Update",
                [nameof(HttpJobForm.OnValidSubmit)] = EventCallback.Factory.Create(this, submit)
            }));
        });

        public List<Exception> Exceptions { get; } = [];
        public IEnumerable<RenderTreeFrame> RenderedFrames => Frames(_root);

        public Task SetErrorAsync(string message) => Dispatcher.InvokeAsync(() =>
            RenderRootComponentAsync(_root, ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(HttpJobForm.ErrorMessage)] = message
            })));

        public bool SubmitDisabled => Frames(_root).Any(frame =>
            frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == "disabled" &&
            frame.AttributeValue is true);

        public Task SubmitAsync() => Dispatcher.InvokeAsync(() =>
        {
            var handler = Frames(_root).Single(frame =>
                frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == "onsubmit");
            return DispatchEventAsync(handler.AttributeEventHandlerId, null, EventArgs.Empty);
        });

        private IEnumerable<RenderTreeFrame> Frames(int id)
        {
            var frames = GetCurrentRenderTreeFrames(id);
            for (var i = 0; i < frames.Count; i++)
            {
                var frame = frames.Array[i];
                yield return frame;
                if (frame.FrameType == RenderTreeFrameType.Component)
                {
                    foreach (var child in Frames(frame.ComponentId))
                    {
                        yield return child;
                    }
                }
            }
        }

        // Run the interactive page in-process; this does not emulate browser refresh/navigation.
        protected override IComponent ResolveComponentForRenderMode(Type componentType, int? parentComponentId,
            IComponentActivator componentActivator, IComponentRenderMode renderMode) => componentActivator.CreateInstance(componentType);

        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => Exceptions.Add(exception);
    }
}
#pragma warning restore BL0006
