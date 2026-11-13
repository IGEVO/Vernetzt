using Igevo.Vernetzt.Core.DTOs.Common;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Service that aggregates all data needed for the dashboard in a single call.
/// </summary>
public class DashboardService : IDashboardService
{
    private readonly IClubRepository _clubRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<DashboardService> _logger;

    public DashboardService(
        IClubRepository clubRepository,
        IDepartmentRepository departmentRepository,
        IUserRepository userRepository,
        ILogger<DashboardService> logger)
    {
        _clubRepository = clubRepository;
        _departmentRepository = departmentRepository;
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<DashboardDataDto> GetDashboardDataAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            _logger.LogWarning("Dashboard data requested for unknown user {UserId}", userId);
            return new DashboardDataDto();
        }

        var allClubs = await _clubRepository.GetAllWithDetailsAsync(cancellationToken);
        var allDepartments = await _departmentRepository.GetAllWithDetailsAsync(cancellationToken);

        // Build "My Clubs" list: only clubs where user has a ContactAssignment
        var myClubs = new List<MyClubDto>();

        foreach (var club in allClubs)
        {
            var isContactPerson = club.ContactAssignments?.Any(ca => ca.UserId == userId && ca.ConfirmedAt != null) == true;
            if (!isContactPerson)
                continue;

            // Only show departments the user is actively assigned to
            var clubDepartments = allDepartments
                .Where(d => d.ClubId == club.Id && d.ContactAssignments?.Any(ca => ca.UserId == userId && ca.ConfirmedAt != null) == true)
                .ToList();
            var departmentDtos = clubDepartments.Select(dept => new MyDepartmentDto
            {
                Id = dept.Id,
                Name = dept.Name,
                ParentDepartmentName = dept.ParentDepartment?.Name,
                IsContactPerson = dept.ContactAssignments?.Any(ca => ca.UserId == userId && ca.ConfirmedAt != null) == true,
                CanEdit = true
            }).OrderBy(d => d.ParentDepartmentName ?? d.Name).ThenBy(d => d.ParentDepartmentName == null ? 0 : 1).ThenBy(d => d.Name).ToList();

            myClubs.Add(new MyClubDto
            {
                Id = club.Id,
                Name = club.Name,
                City = club.City,
                IsContactPerson = true,
                CanEdit = true,
                Departments = departmentDtos
            });
        }

        // Also check for departments where user is directly assigned but the parent club is not in myClubs yet
        foreach (var dept in allDepartments)
        {
            var isDeptContact = dept.ContactAssignments?.Any(ca => ca.UserId == userId && ca.ConfirmedAt != null) == true;
            if (!isDeptContact)
                continue;

            var departmentDto = new MyDepartmentDto
            {
                Id = dept.Id,
                Name = dept.Name,
                ParentDepartmentName = dept.ParentDepartment?.Name,
                IsContactPerson = true,
                CanEdit = true
            };

            var existingClub = myClubs.FirstOrDefault(c => c.Id == dept.ClubId);
            if (existingClub != null)
            {
                // Clubs added by this loop collect all directly assigned departments and groups
                if (!existingClub.IsContactPerson && existingClub.Departments.All(d => d.Id != dept.Id))
                {
                    existingClub.Departments.Add(departmentDto);
                }

                continue;
            }

            var parentClub = allClubs.FirstOrDefault(c => c.Id == dept.ClubId);
            if (parentClub == null)
                continue;

            myClubs.Add(new MyClubDto
            {
                Id = parentClub.Id,
                Name = parentClub.Name,
                City = parentClub.City,
                IsContactPerson = false,
                CanEdit = false,
                Departments = new List<MyDepartmentDto> { departmentDto }
            });
        }

        _logger.LogInformation(
            "Dashboard data loaded for user {UserId}: {MyClubCount} own clubs",
            userId, myClubs.Count);

        return new DashboardDataDto
        {
            UserDisplayName = $"{user.FirstName} {user.LastName}".Trim(),
            MyClubs = myClubs.OrderBy(c => c.Name).ToList(),
            PendingAssignmentCount = allClubs.Sum(c => c.ContactAssignments?.Count(ca => ca.UserId == userId && ca.ConfirmedAt == null) ?? 0)
                + allDepartments.Sum(d => d.ContactAssignments?.Count(ca => ca.UserId == userId && ca.ConfirmedAt == null) ?? 0)
        };
    }
}
