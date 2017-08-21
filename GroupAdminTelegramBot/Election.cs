using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GroupAdminTelegramBot
{
    class Election
    {
        private int id;
        public int Id { get { return this.id; } set { this.id = value; } }

        private string description;
        public string Description { get { return this.description; } set { this.description = value; } }

        private List<KeyValuePair<string, int>> options;
        public List<KeyValuePair<string, int>> Options { get { return this.options; } set { this.options = value; } }

        public Election(int id)
        {
            this.id = id;
            this.description = null;
            this.options = new List<KeyValuePair<string, int>>();
        }
    }
}