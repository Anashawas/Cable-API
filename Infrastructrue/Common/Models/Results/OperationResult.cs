namespace Infrastructrue.Common.Models.Results;

public class OperationResult
{
    public bool IsSuccessful { get; set; }
    public Error Error { get; set; }
    public DateTime RequestTime { get; set; }
}

public class OperationResult<T> : OperationResult
{
    public T Result { get; set; }
}

public class Error
{
    public string ErrorType { get; set; } = null!;
    public string ErrorMessage { get; set; } = null!;
    public string ErrorStackTrace { get; set; } = null!;


}
