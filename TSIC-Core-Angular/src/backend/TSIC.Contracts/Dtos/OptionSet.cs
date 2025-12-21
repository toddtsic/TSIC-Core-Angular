using System.Collections.Generic;

namespace TSIC.Contracts.Dtos;

public sealed class OptionSet
{
    public string Key { get; set; } = string.Empty;
    public string Provider { get; set; } = "Jobs.JsonOptions";
    public bool ReadOnly { get; set; } = false;
    public List<ProfileFieldOption> Values { get; set; } = new();
}

public sealed class OptionSetUpdateRequest
{
    public List<ProfileFieldOption> Values { get; set; } = new();
}

public sealed class RenameOptionSetRequest
{
    public string NewKey { get; set; } = string.Empty;
}
