namespace ConsoleApp.SipOperations
{
    public interface ISipOperation
    {
        Task<bool> ExecuteAsync(CancellationToken cancellationToken);
        string OperationName { get; }
    }
}