using robeem.Application.Attendance;

namespace robeem.Tests.Api;

/// <summary>An <see cref="IUploadDeletionService"/> that answers what it is told to and remembers what it was asked.</summary>
internal sealed class FakeUploadDeletion : IUploadDeletionService
{
    public UploadDeletionResult Result { get; set; } = new(UploadDeletionOutcome.Deleted, PunchesDeleted: 3);

    public List<int> Asked { get; } = [];

    public Task<UploadDeletionResult> DeleteAsync(int uploadId, CancellationToken cancellationToken)
    {
        Asked.Add(uploadId);
        return Task.FromResult(Result);
    }
}
