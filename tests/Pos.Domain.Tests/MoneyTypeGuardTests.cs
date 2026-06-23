using System.Reflection;
using Pos.Domain.Common;

namespace Pos.Domain.Tests;

/// <summary>
/// B13 — quy tắc tiền tệ: toàn hệ thống dùng <c>decimal</c>, KHÔNG <c>double/float</c> (sai = sai tiền khách).
/// Test này canh gác toàn bộ entity domain để không ai vô tình thêm field nhị phân dấu phẩy động.
/// </summary>
public class MoneyTypeGuardTests
{
    [Fact]
    public void DomainEntities_HaveNo_DoubleOrFloat_Members()
    {
        var assembly = typeof(EntityBase).Assembly;
        var banned = new[] { typeof(double), typeof(float), typeof(double?), typeof(float?) };

        var offenders = new List<string>();
        foreach (var type in assembly.GetTypes().Where(t => t.IsClass))
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.DeclaredOnly;

            foreach (var p in type.GetProperties(flags))
                if (banned.Contains(p.PropertyType))
                    offenders.Add($"{type.Name}.{p.Name} : {p.PropertyType.Name}");

            foreach (var f in type.GetFields(flags).Where(f => !f.Name.Contains('<'))) // bỏ backing field
                if (banned.Contains(f.FieldType))
                    offenders.Add($"{type.Name}.{f.Name} : {f.FieldType.Name}");
        }

        Assert.True(offenders.Count == 0,
            "Vi phạm B13 (dùng decimal cho tiền): " + string.Join(", ", offenders));
    }
}
