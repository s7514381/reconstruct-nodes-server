using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReconstructNodesServer.Library.Models.Enum
{
    public enum EStatus : short
    {
        [Display(Name = "刪除")]
        刪除 = -1,

        [Display(Name = "停用")]
        停用 = 0,

        [Display(Name = "啟用")]
        啟用 = 1,
    }
}
