﻿/*
 * Copyright (c) Dominick Baier.  All rights reserved.
 * see license.txt
 */

namespace Thinktecture.IdentityServer.Web.Security
{
    public class TokenResponse
    {
        public string TokenString { get; set; }
        public string TokenType { get; set; }
        public string ContentType { get; set; }
    }
}