namespace Rroms.Web.Common;
/// <summary>
/// The outcome of a business operation, returned by every service method that can fail
/// for a business reason (for example "that table is already booked").
/// </summary>
public class ServiceResult
{
    // Creates a result
    protected ServiceResult(bool succeeded, IReadOnlyList<string> errors)
    {
        Succeeded = succeeded;
        Errors = errors;
    }

    //True when the operation completed and all business rules passed.
    public bool Succeeded { get; }

    //Messages explaining why the operation failed. Empty on success.
    public IReadOnlyList<string> Errors { get; }

    //Creates a successful result.
    public static ServiceResult Success()
    {
        return new ServiceResult(true, Array.Empty<string>());
    }

    /// <summary>Creates a failed result with one or more messages for the user.</summary>
    /// <param name="errors">At least one message describing what went wrong.</param>
    /// <exception cref="ArgumentException">Thrown when no error message is supplied.</exception>
    public static ServiceResult Failure(params string[] errors)
    {
        if (errors.Length == 0)
        {
            // A failure without a reason would leave the user with no idea what to fix.
            throw new ArgumentException("A failed result must include at least one error message.", nameof(errors));
        }

        return new ServiceResult(false, errors);
    }
}

/// <summary>
/// A <see cref="ServiceResult"/> that also carries a value on success,
/// for example the ID of a newly created reservation.
/// </summary>
/// <typeparam name="T">The type of value returned on success.</typeparam>
public class ServiceResult<T> : ServiceResult
{
    private ServiceResult(bool succeeded, T? value, IReadOnlyList<string> errors)
        : base(succeeded, errors)
    {
        Value = value;
    }

    //The value produced by a successful operation. Default when the operation failed.
    public T? Value { get; }

    //Creates a successful result
    public static ServiceResult<T> Success(T value)
    {
        return new ServiceResult<T>(true, value, Array.Empty<string>());
    }

    //Creates a failed result with one or more messages for the user.
    public static new ServiceResult<T> Failure(params string[] errors)
    {
        if (errors.Length == 0)
        {
            throw new ArgumentException("A failed result must include at least one error message.", nameof(errors));
        }

        return new ServiceResult<T>(false, default, errors);
    }
}