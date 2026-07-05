using System.Text.Json;
using FluentAssertions;
using TSIC.API.Services.Shared.Utilities;
using TSIC.Domain.Entities;

namespace TSIC.Tests.AdultRegistration;

/// <summary>
/// Locks the write↔read symmetry the adult "existing registration" (returning-user prefill) path
/// depends on: values written to Registrations columns via <see cref="FormValueMapper.ApplyFormValues"/>
/// must come back out via <see cref="FormValueMapper.BuildFormValuesDictionary"/> under the SAME field
/// names. Regression guard for the bug where an AC3 coach's jersey/shoe selections saved fine but did
/// not rehydrate on return (the read side simply was never called).
/// </summary>
public class FormValueMapperRoundTripTests
{
    private static JsonElement Str(string s) => JsonSerializer.SerializeToElement(s);

    [Fact(DisplayName = "AC3 coach sizes written to columns rehydrate under the same field names")]
    public void ApparelFields_RoundTrip()
    {
        // The AC3 coach schema: jersey + shoe (camelCase name → PascalCase column).
        var mapped = new List<(string Name, string DbColumn)>
        {
            ("jerseySize", "JerseySize"),
            ("shoes", "Shoes"),
        };

        var incoming = new Dictionary<string, JsonElement>
        {
            ["jerseySize"] = Str("adult medium"),
            ["shoes"] = Str("7.5"),
        };

        var reg = new Registrations();
        var nameToProperty = mapped.ToDictionary(m => m.Name, m => m.DbColumn, StringComparer.OrdinalIgnoreCase);
        var writable = FormValueMapper.BuildWritablePropertyMap();

        FormValueMapper.ApplyFormValues(reg, incoming, nameToProperty, writable);

        // Columns took the values...
        reg.JerseySize.Should().Be("adult medium");
        reg.Shoes.Should().Be("7.5");

        // ...and reading back yields the SAME field names with the SAME values (the rehydration path).
        var readBack = FormValueMapper.BuildFormValuesDictionary(reg, mapped);
        readBack.Should().ContainKey("jerseySize");
        readBack.Should().ContainKey("shoes");
        readBack["jerseySize"].GetString().Should().Be("adult medium");
        readBack["shoes"].GetString().Should().Be("7.5");
    }

    [Fact(DisplayName = "Null columns are omitted from the rehydrated form values (no phantom blanks)")]
    public void NullColumns_Omitted()
    {
        var mapped = new List<(string Name, string DbColumn)>
        {
            ("jerseySize", "JerseySize"),
            ("shoes", "Shoes"),
        };

        var reg = new Registrations { JerseySize = "LG", Shoes = null };

        var readBack = FormValueMapper.BuildFormValuesDictionary(reg, mapped);
        readBack.Should().ContainKey("jerseySize");
        readBack.Should().NotContainKey("shoes"); // null column → absent, not an empty string
    }
}
