using TSIC.Application.DTOs;
using TSIC.Application.Services;
using System.Collections.Generic;
using System.Threading.Tasks;
using TSIC.Domain.Constants;
using TSIC.Infrastructure.Data.SqlDbContext;
using Microsoft.EntityFrameworkCore;

namespace TSIC.Infrastructure.Services
{
    public class RoleLookupService : IRoleLookupService
    {
        private readonly SqlDbContext _context;

        public RoleLookupService(SqlDbContext context)
        {
            _context = context;
        }

        public async Task<List<RegistrationRoleDto>> GetRegistrationsForUserAsync(string userId)
        {
            var model = new List<RegistrationRoleDto>();

            var lSuperUserRoles = await (
                from r in _context.Registrations
                join role in _context.AspNetRoles on r.RoleId equals role.Id
                join j in _context.Jobs on r.JobId equals j.JobId
                join jdo in _context.JobDisplayOptions on j.JobId equals jdo.JobId
                where
                    (r.UserId == userId)
                    //&& (r.RegistrationId.ToString() != RegistrationId)
                    && (r.BActive == true)
                    && DateTime.Now < j.ExpiryAdmin
                    && role.Id == RoleConstants.Superuser
                orderby j.JobName
                select new RegistrationDto(r.RegistrationId.ToString(), j.JobName, $"{TsicConstants.BaseUrlStatics}BannerFiles/{jdo.LogoHeader}")
            ).AsNoTracking().ToListAsync();

            if (lSuperUserRoles.Count > 0)
            {
                model.Add(new RegistrationRoleDto("Superuser", lSuperUserRoles));
            }

            var lSuperDirectorRoles = await (
                from r in _context.Registrations
                join role in _context.AspNetRoles on r.RoleId equals role.Id
                join j in _context.Jobs on r.JobId equals j.JobId
                join jdo in _context.JobDisplayOptions on j.JobId equals jdo.JobId
                where
                    (r.UserId == userId)
                    //&& (r.RegistrationId.ToString() != RegistrationId)
                    && (r.BActive == true)
                    && DateTime.Now < j.ExpiryAdmin
                    && role.Name == "SuperDirector"
                orderby j.JobName
                select new RegistrationDto(
                    r.RegistrationId.ToString(),
                    j.JobName,
                    $"{TsicConstants.BaseUrlStatics}BannerFiles/{jdo.LogoHeader}"
                )
            ).AsNoTracking().ToListAsync();

            if (lSuperDirectorRoles.Count > 0)
            {
                model.Add(new RegistrationRoleDto("SuperDirector", lSuperDirectorRoles));
            }

            var lDirectorRoles = await (
                from r in _context.Registrations
                join role in _context.AspNetRoles on r.RoleId equals role.Id
                join j in _context.Jobs on r.JobId equals j.JobId
                join jdo in _context.JobDisplayOptions on j.JobId equals jdo.JobId
                where
                    (r.UserId == userId)
                    //&& (r.RegistrationId.ToString() != RegistrationId)
                    && (r.BActive == true)
                    && DateTime.Now < j.ExpiryAdmin
                    && role.Id == RoleConstants.Director
                orderby j.JobName
                select new RegistrationDto(
                    r.RegistrationId.ToString(),
                    j.JobName,
                    $"{TsicConstants.BaseUrlStatics}BannerFiles/{jdo.LogoHeader}"
                )
            ).AsNoTracking().ToListAsync();

            if (lDirectorRoles.Count > 0)
            {
                model.Add(new RegistrationRoleDto("Director", lDirectorRoles));
            }

            var lFamilyRoles = await (
                from r in _context.Registrations
                join u in _context.AspNetUsers on r.UserId equals u.Id
                join t in _context.Teams on r.AssignedTeamId equals t.TeamId
                join ag in _context.Agegroups on t.AgegroupId equals ag.AgegroupId
                join role in _context.AspNetRoles on r.RoleId equals role.Id
                join j in _context.Jobs on r.JobId equals j.JobId
                join jdo in _context.JobDisplayOptions on j.JobId equals jdo.JobId
                where
                    (r.FamilyUserId == userId)
                    //&& (r.RegistrationId.ToString() != RegistrationId)
                    && (r.BActive == true)
                    && DateTime.Now < j.ExpiryUsers
                    && role.Id == RoleConstants.Player
                orderby j.JobName
                select new RegistrationDto(

                    r.RegistrationId.ToString(),
                    $"{j.JobName}:{u.FirstName} {u.LastName}:{ag.AgegroupName}:{t.TeamName}",
                    $"{TsicConstants.BaseUrlStatics}BannerFiles/{jdo.LogoHeader}"
                )
            ).AsNoTracking().ToListAsync();

            if (lFamilyRoles.Count > 0)
            {
                model.Add(new RegistrationRoleDto("Player", lFamilyRoles));
            }

            var lClubRepRoles = await (
                from r in _context.Registrations
                join role in _context.AspNetRoles on r.RoleId equals role.Id
                join j in _context.Jobs on r.JobId equals j.JobId
                join jdo in _context.JobDisplayOptions on j.JobId equals jdo.JobId
                where
                    (r.UserId == userId)
                    //&& (r.RegistrationId.ToString() != RegistrationId)
                    && (r.BActive == true)
                    && DateTime.Now < j.ExpiryUsers
                    && role.Id == RoleConstants.ClubRep
                orderby j.JobName
                select new RegistrationDto(

                    r.RegistrationId.ToString(),
                    j.JobName,
                    $"{TsicConstants.BaseUrlStatics}BannerFiles/{jdo.LogoHeader}"
                )
            ).AsNoTracking().ToListAsync();

            if (lClubRepRoles.Count > 0)
            {
                model.Add(new RegistrationRoleDto("Club Rep", lClubRepRoles));
            }

            var lStaffRoles = await (
                from r in _context.Registrations
                join role in _context.AspNetRoles on r.RoleId equals role.Id
                join j in _context.Jobs on r.JobId equals j.JobId
                join jdo in _context.JobDisplayOptions on j.JobId equals jdo.JobId
                join t in _context.Teams on r.AssignedTeamId equals t.TeamId
                join ag in _context.Agegroups on t.AgegroupId equals ag.AgegroupId
                where
                    (r.UserId == userId)
                    //&& (r.RegistrationId.ToString() != RegistrationId)
                    && (r.BActive == true)
                    && DateTime.Now < j.ExpiryUsers
                    && role.Id == RoleConstants.Staff
                orderby j.JobName
                select new RegistrationDto(
                    r.RegistrationId.ToString(),
                    $"{j.JobName}:{ag.AgegroupName}:{t.TeamName}",
                    $"{TsicConstants.BaseUrlStatics}BannerFiles/{jdo.LogoHeader}"
                )
            ).AsNoTracking().ToListAsync();

            if (lStaffRoles.Count > 0)
            {
                model.Add(new RegistrationRoleDto("Staff", lStaffRoles));
            }

            var lStoreAdminRoles = await (
                from r in _context.Registrations
                join role in _context.AspNetRoles on r.RoleId equals role.Id
                join j in _context.Jobs on r.JobId equals j.JobId
                join jdo in _context.JobDisplayOptions on j.JobId equals jdo.JobId
                where
                    (r.UserId == userId)
                    //&& (r.RegistrationId.ToString() != RegistrationId)
                    && (r.BActive == true)
                    && DateTime.Now < j.ExpiryUsers
                    && r.RoleId == RoleConstants.StoreAdmin
                orderby j.JobName
                select new RegistrationDto(
                    r.RegistrationId.ToString(),
                    $"{j.JobName}",
                    $"{TsicConstants.BaseUrlStatics}BannerFiles/{jdo.LogoHeader}"
                )
            ).AsNoTracking().ToListAsync();

            if (lStoreAdminRoles.Count > 0)
            {
                model.Add(new RegistrationRoleDto("Store Admin", lStoreAdminRoles));
            }

            var lRefAssignorRoles = await (
                from r in _context.Registrations
                join role in _context.AspNetRoles on r.RoleId equals role.Id
                join j in _context.Jobs on r.JobId equals j.JobId
                join jdo in _context.JobDisplayOptions on j.JobId equals jdo.JobId
                where
                    (r.UserId == userId)
                    //&& (r.RegistrationId.ToString() != RegistrationId)
                    && (r.BActive == true)
                    && DateTime.Now < j.ExpiryUsers
                    && r.RoleId == RoleConstants.RefAssignor
                orderby j.JobName
                select new RegistrationDto(

                    r.RegistrationId.ToString(),
                    $"{j.JobName}",
                    $"{TsicConstants.BaseUrlStatics}BannerFiles/{jdo.LogoHeader}"
                )
            ).AsNoTracking().ToListAsync();

            if (lRefAssignorRoles.Count > 0)
            {
                model.Add(new RegistrationRoleDto("Ref Assignor", lRefAssignorRoles));
            }

            var lRefRoles = await (
                from r in _context.Registrations
                join role in _context.AspNetRoles on r.RoleId equals role.Id
                join j in _context.Jobs on r.JobId equals j.JobId
                join jdo in _context.JobDisplayOptions on j.JobId equals jdo.JobId
                where
                    (r.UserId == userId)
                    //&& (r.RegistrationId.ToString() != RegistrationId)
                    && (r.BActive == true)
                    && DateTime.Now < j.ExpiryUsers
                    && r.RoleId == RoleConstants.Referee
                orderby j.JobName
                select new RegistrationDto(
                    r.RegistrationId.ToString(),
                    $"{j.JobName}",
                    $"{TsicConstants.BaseUrlStatics}BannerFiles/{jdo.LogoHeader}"
                )
            ).AsNoTracking().ToListAsync();

            if (lRefRoles.Count > 0)
            {
                model.Add(new RegistrationRoleDto("Referee", lRefRoles));
            }

            return model;
        }
    }
}
