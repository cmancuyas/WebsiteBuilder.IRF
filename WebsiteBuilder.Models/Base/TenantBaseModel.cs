using System;
using System.Collections.Generic;
using System.Text;

namespace WebsiteBuilder.Models.Base
{
    public abstract class TenantBaseModel : BaseModel
    {
        public Guid TenantId { get; set; }
    }
}
