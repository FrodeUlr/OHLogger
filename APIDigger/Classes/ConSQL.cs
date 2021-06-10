﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIDigger.Classes
{
    internal class ConSQL
    {
		// Get the connection string from App config file.
		protected static internal string GetConnectionString()
		{
			string connString = "Server=192.168.1.161; database=OpenHAB; UID=openhab; password=567814";
			return connString;
		}
		protected static internal string GetConnectionString_up()
		{
			string connString = "Server=192.168.1.161; database=OpenHAB; UID=" + Properties.Settings.Default.UserSql + 
								"; password=" + Properties.Settings.Default.PassSql;
			return connString;
		}
	}
	class CheckSql
    {

		
	}
}