using System.Data;
using Dapper;

namespace Server.Infrastructure;

public class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
{
    public override DateTimeOffset Parse(object value) =>
        new(DateTime.SpecifyKind((DateTime)value, DateTimeKind.Unspecified), TimeSpan.FromHours(7));

    public override void SetValue(IDbDataParameter parameter, DateTimeOffset value) =>
        parameter.Value = DateTime.SpecifyKind(value.DateTime, DateTimeKind.Unspecified);
}