using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChatdollKit.Dialog
{
    public class Response
    {
        public string Id { get; }
        public DateTime Timestamp { get; }
        public string Text { get; set; }
        public virtual object Payloads { get; set; }

        public Response(string id)
        {
            Id = id;
            Timestamp = DateTime.UtcNow;
        }
    }
}
