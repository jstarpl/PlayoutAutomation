﻿using System.Collections.Generic;
using System.Xml.Serialization;
using Newtonsoft.Json;
using TAS.Remoting.Server;
using TAS.Common;
using TAS.Common.Interfaces;
using TAS.Common.Interfaces.Security;

namespace TAS.Server.Security
{
    /// <summary>
    /// base class for User and Group
    /// </summary>
    public abstract class SecurityObjectBase: DtoBase, ISecurityObject
    {
        private string _name;

        protected SecurityObjectBase(IAuthenticationService authenticationService)
        {
            AuthenticationService = authenticationService;
            FieldLengths = EngineController.Database.SecurityObjectFieldLengths;
        }

        public abstract SecurityObjectType SecurityObjectTypeType { get; }

        [JsonProperty, XmlIgnore]
        public IAuthenticationService AuthenticationService { get; set; }

        [XmlIgnore]
        public ulong Id { get; set; }

        [JsonProperty]
        public string Name
        {
            get => _name;
            set => SetField(ref _name, value);
        }

        public IDictionary<string, int> FieldLengths { get; }

        public abstract void Save();

        public abstract void Delete();

    }
}
