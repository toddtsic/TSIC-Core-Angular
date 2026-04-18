
declare @rolename nvarchar(256) = 'Director'

select 
	jt.JobTypeDesc,
	j.jobName,
	jm.menuID,
	jmiP.[Text] as L1Text,
	jmiC.[Text] as L2Text,
	jmiC.Controller,
	jmiC.Action
from
	Jobs.JobMenus jm
	inner join Jobs.Jobs j on jm.jobId = j.JobId
	inner join reference.JobTypes jt on j.jobTypeId = jt.jobTypeId
	inner join dbo.AspNetRoles roles on jm.RoleID = roles.Id
	inner join Jobs.JobMenu_Items jmiP on jm.MenuId = jmiP.menuID and jmiP.parentMenuItemID is null
	inner join Jobs.JobMenu_Items jmiC on jmiC.menuId = jmiP.menuId and  jmiC.parentMenuItemID = jmiP.menuItemID
where 
	jm.menuTypeId = 6
	and roles.Name = @rolename
	and j.year in ('2026', '2027')
	and jmiP.[Text] != 'new parent item'
	and jmiC.[Text] != 'new child item'
order by
	jt.JobTypeDesc,
	j.jobName,
	jmiP.[Text],
	jmiC.[index]ho