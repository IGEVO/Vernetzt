namespace Igevo.Vernetzt.Core.Enums;

/// <summary>
/// Defines user claims/roles for authorization.
/// </summary>
public enum UserClaim
{
    /// <summary>
    /// System administrator with full access to all features and system configuration.
    /// </summary>
    SystemAdmin = 0,

    /// <summary>
    /// Manager - one level below admin. Can edit everything, create new clubs, club types, departments and users.
    /// </summary>
    Manager = 1,

    /// <summary>
    /// Data Reader - can read all data with visibility level Internal. All authenticated users have base permissions.
    /// </summary>
    DataReader = 2
}

// Note: All authenticated users have base permissions by default:
// - Can edit own data and assigned clubs/departments
// - Can create departments in own clubs
// - Can create users or assign existing users to own clubs/departments
// - Can see all clubs/departments and user data with visibility level AllUsers
