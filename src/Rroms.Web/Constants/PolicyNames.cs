using System.Globalization;

namespace Rroms.Web.Constants;

/// <summary>
/// Names of the authorization policies registered in Program.cs (YY-3).
/// </summary>

public static class PolicyNames
{
    //Administrator role only
    public const string AdministratorOnly = "AdministratorOnly";

    //Staff or Administrator. Admins can do everything staff can do
    public const string StaffOrAdministrator = "StaffOrAdministrator";

    //Customer role only
    public const string CustomerOnly = "CustomerOnly";
}
