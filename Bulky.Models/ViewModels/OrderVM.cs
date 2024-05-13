using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bulky.Models.ViewModels
{
    public  class OrderVM
    {
        public OrderOfHeader? OrderOfHeader { get; set; }
        public IEnumerable<OrderDetail>? orderDetails { get; set; }
    }
}
