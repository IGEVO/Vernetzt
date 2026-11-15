namespace Igevo.Vernetzt.Core.DTOs.Common;

/// <summary>
/// Base class for search/filter parameters with pagination.
/// </summary>
public class SearchParameters
{
    /// <summary>
    /// Search term for filtering by name or other text fields.
    /// </summary>
    public string? SearchTerm { get; set; }

    /// <summary>
    /// Page number (1-based).
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Number of items per page.
    /// </summary>
    public int PageSize { get; set; } = 20;

    /// <summary>
    /// Property name to sort by.
    /// </summary>
    public string? SortBy { get; set; }

    /// <summary>
    /// Sort direction (asc/desc).
    /// </summary>
    public string SortDirection { get; set; } = "asc";

    /// <summary>
    /// Calculate skip count for pagination.
    /// </summary>
    public int Skip => (Page - 1) * PageSize;

    /// <summary>
    /// Validate and normalize parameters.
    /// </summary>
    public void Normalize()
    {
        if (Page < 1) Page = 1;
        if (PageSize < 1) PageSize = 20;
        if (PageSize > 100) PageSize = 100;
        SortDirection = SortDirection?.ToLower() == "desc" ? "desc" : "asc";
    }
}

/// <summary>
/// Search parameters specific to clubs.
/// </summary>
public class ClubSearchParameters : SearchParameters
{
    /// <summary>
    /// Filter by club type ID.
    /// </summary>
    public Guid? ClubTypeId { get; set; }

    /// <summary>
    /// Filter by city.
    /// </summary>
    public string? City { get; set; }
}

/// <summary>
/// Search parameters for departments.
/// </summary>
public class DepartmentSearchParameters : SearchParameters
{
    /// <summary>
    /// Filter by club ID.
    /// </summary>
    public Guid? ClubId { get; set; }

    /// <summary>
    /// Filter by city.
    /// </summary>
    public string? City { get; set; }
}

/// <summary>
/// Search parameters for users (contact directory).
/// </summary>
public class ContactSearchParameters : SearchParameters
{
    /// <summary>
    /// Filter by club ID.
    /// </summary>
    public Guid? ClubId { get; set; }

    /// <summary>
    /// Filter by department ID.
    /// </summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>
    /// Filter by position ID.
    /// </summary>
    public Guid? PositionId { get; set; }
}

/// <summary>
/// Paged result wrapper.
/// </summary>
public class PagedResult<T>
{
    public IEnumerable<T> Items { get; set; } = Array.Empty<T>();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;

    public static PagedResult<T> Create(IEnumerable<T> items, int totalCount, int page, int pageSize)
    {
        return new PagedResult<T>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }
}

/// <summary>
/// Club count by type.
/// </summary>
public class ClubTypeStats
{
    public Guid? ClubTypeId { get; set; }
    public string ClubTypeName { get; set; } = string.Empty;
    public int ClubCount { get; set; }
}

/// <summary>
/// Combined dashboard data returned by a single API call.
/// Contains user's own clubs/departments for quick navigation.
/// </summary>
public class DashboardDataDto
{
    public string UserDisplayName { get; set; } = string.Empty;
    public List<MyClubDto> MyClubs { get; set; } = new();

    /// <summary>
    /// Assignments of the user that still wait for consent (and the choice of visibility).
    /// </summary>
    public int PendingAssignmentCount { get; set; }
}

/// <summary>
/// A club the current user is associated with (as contact person or manager).
/// </summary>
public class MyClubDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? City { get; set; }
    public bool IsContactPerson { get; set; }
    public bool CanEdit { get; set; }
    public List<MyDepartmentDto> Departments { get; set; } = new();
}

/// <summary>
/// A department within a user's club, shown for quick navigation.
/// </summary>
public class MyDepartmentDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Name of the parent department if this entry is a group.</summary>
    public string? ParentDepartmentName { get; set; }
    public bool IsContactPerson { get; set; }
    public bool CanEdit { get; set; }
}

/// <summary>
/// Request body for add/update contact person endpoints carrying optional notes.
/// </summary>
public class AssignContactPersonRequest
{
    public string? Notes { get; set; }
}
