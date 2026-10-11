using Rroms.Web.Common;

namespace Rroms.Web.Services;

/// <summary>
/// Validates, resizes and stores dish photos. Owner: YJ (Sprint 3, YJ-1).
/// </summary>
/// <remarks>
/// Works with a plain <see cref="Stream"/> instead of the web framework's upload type, 
/// so the rules can be unit tested without a browser. 
/// The admin page passes in the uploaded file's stream.
/// </remarks>
public interface IMenuImageStorage
{
    /// <summary>
    /// Checks the upload (not empty, small enough, allowed file type, really an image), 
    /// scales it down if it is wider than the limit and saves it under a newly generated name.
    /// </summary>
    /// <param name="content">The uploaded file's bytes. The stream must know its length.</param>
    /// <param name="originalFileName">The name the user's computer gave the file. 
    /// Only its extension is used.</param>
    /// <returns>The stored file name (not a path) on success.</returns>
    Task<ServiceResult<string>> SaveAsync(Stream content, string originalFileName);

    /// <summary>Deletes a stored photo. Does nothing when the file does not exist.</summary>
    /// <param name="fileName">A stored file name as returned by <see cref="SaveAsync"/>.</param>
    void Delete(string fileName);

    /// <summary>Returns the web address a page uses to show a stored photo, 
    /// for example /uploads/menu/abc.png.</summary>
    /// <param name="fileName">A stored file name as returned by <see cref="SaveAsync"/>.</param>
    string GetPublicUrl(string fileName);
}