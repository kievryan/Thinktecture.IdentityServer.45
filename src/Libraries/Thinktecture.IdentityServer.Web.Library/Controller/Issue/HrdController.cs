﻿/*
 * Copyright (c) Dominick Baier.  All rights reserved.
 * see license.txt
 */

using System;
using System.ComponentModel.Composition;
using System.IdentityModel.Selectors;
using System.IdentityModel.Services;
using System.IdentityModel.Services.Configuration;
using System.IdentityModel.Tokens;
using System.Linq;
using System.Security.Claims;
using System.Web;
using System.Web.Mvc;
using Newtonsoft.Json.Linq;
using Thinktecture.IdentityServer.Models;
using Thinktecture.IdentityServer.Repositories;
using Thinktecture.IdentityServer.TokenService;
using Thinktecture.IdentityServer.Web.ActionResults;

namespace Thinktecture.IdentityServer.Web.Controllers
{
    public class HrdController : Controller
    {
        [Import]
        public IConfigurationRepository ConfigurationRepository { get; set; }

        [Import]
        public IIdentityProviderRepository IdentityProviderRepository { get; set; }


        public HrdController()
        {
            Container.Current.SatisfyImportsOnce(this);
        }

        public HrdController(IConfigurationRepository configurationRepository, IIdentityProviderRepository identityProviderRepository)
        {
            IdentityProviderRepository = identityProviderRepository;
            ConfigurationRepository = configurationRepository;
        }

        public ActionResult Issue()
        {
            Tracing.Verbose("HRD endpoint called.");

            if (!ConfigurationRepository.Endpoints.WSFederationHrd)
            {
                return new HttpNotFoundResult();
            }

            var message = WSFederationMessage.CreateFromUri(HttpContext.Request.Url);

            // sign in 
            var signinMessage = message as SignInRequestMessage;
            if (signinMessage != null)
            {
                return ProcessSignInRequest(signinMessage);
            }

            return View("Error");
        }

        [HttpPost]
        [ActionName("Issue")]
        public ActionResult IssueResponse()
        {
            var fam = new WSFederationAuthenticationModule();
            fam.FederationConfiguration = new FederationConfiguration();

            if (fam.CanReadSignInResponse(Request))
            {
                var responseMessage = fam.GetSignInResponseMessage(Request);
                return ProcessSignInResponse(responseMessage, fam.GetSecurityToken(Request));
            }

            return View("Error");
        }

        #region Helper
        private ActionResult ProcessSignInRequest(SignInRequestMessage message)
        {
            if (!string.IsNullOrWhiteSpace(message.HomeRealm))
            {
                return RedirectToIdentityProvider(message);
            }
            else
            {
                return ShowHomeRealmSelection();
            }

            //// issue token and create ws-fed response
            //var response = FederatedPassiveSecurityTokenServiceOperations.ProcessSignInRequest(
            //    message,
            //    principal as ClaimsPrincipal,
            //    TokenServiceConfiguration.Current.CreateSecurityTokenService());

            //// set cookie for single-sign-out
            //new SignInSessionsManager(HttpContext, ConfigurationRepository.Configuration.MaximumTokenLifetime)
            //    .AddRealm(response.BaseUri.AbsoluteUri);

            //return new WSFederationResult(response);
        }

        private ActionResult ProcessSignInResponse(SignInResponseMessage responseMessage, SecurityToken token)
        {
            var principal = ValidateToken(token);
            var issuerName = principal.Claims.First().Issuer;

            principal.Identities.First().AddClaim(
                new Claim(Constants.Claims.IdentityProvider, issuerName, ClaimValueTypes.String, Constants.LocalIssuer));

            var context = GetContextCookie();
            var message = new SignInRequestMessage(new Uri("http://foo"), context.Realm);
            message.Context = context.Wctx;

            // issue token and create ws-fed response
            var wsFedResponse = FederatedPassiveSecurityTokenServiceOperations.ProcessSignInRequest(
                message,
                principal,
                TokenServiceConfiguration.Current.CreateSecurityTokenService());

            //// set cookie for single-sign-out
            //new SignInSessionsManager(HttpContext, ConfigurationRepository.Configuration.MaximumTokenLifetime)
            //    .AddRealm(response.BaseUri.AbsoluteUri);

            return new WSFederationResult(wsFedResponse);
        }

        private ClaimsPrincipal ValidateToken(SecurityToken token)
        {
            var config = new SecurityTokenHandlerConfiguration();
            config.AudienceRestriction.AudienceMode = AudienceUriMode.Always;
            config.AudienceRestriction.AllowedAudienceUris.Add(new Uri(ConfigurationRepository.Configuration.IssuerUri));

            var registry = new IdentityProviderIssuerNameRegistry(IdentityProviderRepository.GetAll());
            config.IssuerNameRegistry = registry;
            config.CertificateValidationMode = System.ServiceModel.Security.X509CertificateValidationMode.None;

            var handler = SecurityTokenHandlerCollection.CreateDefaultSecurityTokenHandlerCollection(config);
            var identity = handler.ValidateToken(token).First();

            return new ClaimsPrincipal(identity);
        }

       
        private ActionResult ShowHomeRealmSelection()
        {
            throw new System.NotImplementedException();
        }

        private ActionResult RedirectToIdentityProvider(SignInRequestMessage request)
        {
            IdentityProvider idp = null;
            if (IdentityProviderRepository.TryGet(request.HomeRealm, out idp))
            {
                return RedirectToIdentityProvider(idp, request);
            }

            return View("Error");
        }

        private ActionResult RedirectToIdentityProvider(IdentityProvider identityProvider, SignInRequestMessage request)
        {
            var message = new SignInRequestMessage(new Uri(identityProvider.WSFederationEndpoint), ConfigurationRepository.Configuration.IssuerUri);
            SetContextCookie(request.Context, request.Realm);

            return new RedirectResult(message.WriteQueryString());
        }

        private void SetContextCookie(string wctx, string realm)
        {
            var j = JObject.FromObject(new Context { Wctx = wctx, Realm = realm });

            var cookie = new HttpCookie("rdwcontext", j.ToString());
            cookie.Secure = true;
            cookie.HttpOnly = true;

            Response.Cookies.Add(cookie);
        }

        private Context GetContextCookie()
        {
            var cookie = Request.Cookies["rdwcontext"];
            if (cookie == null)
            {
                throw new InvalidOperationException("cookie");
            }

            var json = JObject.Parse(HttpUtility.UrlDecode(cookie.Value));
            return json.ToObject<Context>();
        }

        internal class Context
        {
            public string Wctx { get; set; }
            public string Realm { get; set; }
        }

        #endregion
    }
}
