using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Validation;

namespace Microsoft.Diagnostics.EventFlow.Outputs
{
    public class SqlTableOutputFactory : IPipelineItemFactory<SqlTableOutput>
    {
        public SqlTableOutput CreateItem(IConfiguration configuration, IHealthReporter healthReporter)
        {
            Requires.NotNull(configuration, nameof(configuration));
            Requires.NotNull(healthReporter, nameof(healthReporter));

            return new SqlTableOutput(configuration, healthReporter);
        }
    }
}
