﻿// Copyright (c) Brock Allen & Dominick Baier. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.


using FluentAssertions;
using IdentityModel;
using IdentityServer4.Configuration;
using IdentityServer4.Models;
using IdentityServer4.Services.Default;
using IdentityServer4.UnitTests.Common;
using IdentityServer4.Validation;
using System;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Xunit;

namespace IdentityServer4.UnitTests.Services.Default
{
    public class DefaultClaimsServiceTests
    {
        DefaultClaimsService _subject;
        MockProfileService _mockMockProfileService = new MockProfileService();

        ClaimsPrincipal _user;
        Client _client;
        ValidatedRequest _validatedRequest;
        Resources _resources = new Resources();

        public DefaultClaimsServiceTests()
        {
            _validatedRequest = new ValidatedRequest
            {
            };

            _client = new Client
            {
                ClientId = "client",
                Claims = { new Claim("some_claim", "some_claim_value") }
            };

            _user = IdentityServerPrincipal.Create("bob", "bob", new Claim[] {
                new Claim("foo", "foo1"),
                new Claim("foo", "foo2"),
                new Claim("bar", "bar1"),
                new Claim("bar", "bar2"),
                new Claim(JwtClaimTypes.AuthenticationContextClassReference, "acr1")
            });

            _subject = new DefaultClaimsService(_mockMockProfileService, TestLogger.Create<DefaultClaimsService>());
        }

        [Fact]
        public async Task GetIdentityTokenClaimsAsync_should_return_standard_user_claims()
        {
            var claims = await _subject.GetIdentityTokenClaimsAsync(_user, _client, _resources, false, _validatedRequest);

            var types = claims.Select(x => x.Type);
            types.Should().Contain(JwtClaimTypes.Subject);
            types.Should().Contain(JwtClaimTypes.AuthenticationTime);
            types.Should().Contain(JwtClaimTypes.IdentityProvider);
            types.Should().Contain(JwtClaimTypes.AuthenticationMethod);
            types.Should().Contain(JwtClaimTypes.AuthenticationContextClassReference);
        }

        [Fact]
        public async Task GetIdentityTokenClaimsAsync_should_return_minimal_claims_when_includeAllIdentityClaims_is_false()
        {
            _resources.IdentityResources.Add(new IdentityResource("id_scope", new[] { "foo" }));

            var claims = await _subject.GetIdentityTokenClaimsAsync(_user, _client, _resources, false, _validatedRequest);

            _mockMockProfileService.GetProfileWasCalled.Should().BeFalse();
        }

        [Fact]
        public async Task GetIdentityTokenClaimsAsync_should_return_all_claims_when_includeAllIdentityClaims_is_true()
        {
            _resources.IdentityResources.Add(new IdentityResource("id_scope", new[] { "foo" }));
            _mockMockProfileService.ProfileClaims.Add(new Claim("foo", "foo1"));

            var claims = await _subject.GetIdentityTokenClaimsAsync(_user, _client, _resources, true, _validatedRequest);

            _mockMockProfileService.GetProfileWasCalled.Should().BeTrue();
            _mockMockProfileService.ProfileContext.RequestedClaimTypes.Should().Contain("foo");
        }

        [Fact]
        public async Task GetIdentityTokenClaimsAsync_should_filter_protocol_claims_from_profile_service()
        {
            _resources.IdentityResources.Add(new IdentityResource("id_scope", new[] { "foo" }));
            _mockMockProfileService.ProfileClaims.Add(new Claim("aud", "bar"));

            var claims = await _subject.GetIdentityTokenClaimsAsync(_user, _client, _resources, true, _validatedRequest);

            claims.Count(x=>x.Type == "aud" && x.Value == "bar").Should().Be(0);
        }

        [Fact]
        public async Task GetAccessTokenClaimsAsync_should_contain_client_id()
        {
            var claims = await _subject.GetAccessTokenClaimsAsync(_user, _client, _resources, _validatedRequest);

            claims.Where(x => x.Type == JwtClaimTypes.ClientId && x.Value == _client.ClientId).Count().Should().Be(1);
        }

        [Fact]
        public async Task GetAccessTokenClaimsAsync_client_claims_should_be_prefixed()
        {
            _client.PrefixClientClaims = true;
            var claims = await _subject.GetAccessTokenClaimsAsync(null, _client, _resources, _validatedRequest);

            claims.Where(x => x.Type == "client_some_claim" && x.Value == "some_claim_value").Count().Should().Be(1);
        }

        [Fact]
        public async Task GetAccessTokenClaimsAsync_should_contain_client_claims_when_no_subject()
        {
            _client.PrefixClientClaims = false;
            var claims = await _subject.GetAccessTokenClaimsAsync(null, _client, _resources, _validatedRequest);

            claims.Where(x => x.Type == "some_claim" && x.Value == "some_claim_value").Count().Should().Be(1);
        }

        [Fact]
        public async Task GetAccessTokenClaimsAsync_should_contain_client_claims_when_configured_to_send_client_claims()
        {
            _client.PrefixClientClaims = false;
            _client.AlwaysSendClientClaims = true;

            var claims = await _subject.GetAccessTokenClaimsAsync(_user, _client, _resources, _validatedRequest);

            claims.Where(x => x.Type == "some_claim" && x.Value == "some_claim_value").Count().Should().Be(1);
        }

        [Fact]
        public async Task GetAccessTokenClaimsAsync_should_contain_scopes()
        {
            _resources.IdentityResources.Add(new IdentityResource("id1", new[] { "foo" }));
            _resources.IdentityResources.Add(new IdentityResource("id2", new[] { "bar" }));
            _resources.ApiResources.Add(new ApiResource("api1"));
            _resources.ApiResources.Add(new ApiResource("api2"));

            var claims = await _subject.GetAccessTokenClaimsAsync(_user, _client, _resources, _validatedRequest);

            var scopes = claims.Where(x => x.Type == JwtClaimTypes.Scope).Select(x => x.Value);
            scopes.Count().Should().Be(4);
            scopes.ToArray().ShouldBeEquivalentTo(new string[] { "api1", "api2", "id1", "id2" });
        }

        [Fact]
        public async Task GetAccessTokenClaimsAsync_should_contain_offline_scope()
        {
            _resources.IdentityResources.Add(new IdentityResource("id1", new[] { "foo" }));
            _resources.IdentityResources.Add(new IdentityResource("id2", new[] { "bar" }));
            _resources.ApiResources.Add(new ApiResource("api1"));
            _resources.ApiResources.Add(new ApiResource("api2"));
            _resources.OfflineAccess = true;

            var claims = await _subject.GetAccessTokenClaimsAsync(_user, _client, _resources, _validatedRequest);

            var scopes = claims.Where(x => x.Type == JwtClaimTypes.Scope).Select(x => x.Value);
            scopes.Should().Contain(IdentityServerConstants.StandardScopes.OfflineAccess);
        }

        [Fact]
        public async Task GetAccessTokenClaimsAsync_should_not_contain_offline_scope_if_no_user()
        {
            _resources.IdentityResources.Add(new IdentityResource("id1", new[] { "foo" }));
            _resources.IdentityResources.Add(new IdentityResource("id2", new[] { "bar" }));
            _resources.ApiResources.Add(new ApiResource("api1"));
            _resources.ApiResources.Add(new ApiResource("api2"));
            _resources.OfflineAccess = true;

            var claims = await _subject.GetAccessTokenClaimsAsync(null, _client, _resources, _validatedRequest);

            var scopes = claims.Where(x => x.Type == JwtClaimTypes.Scope).Select(x => x.Value);
            scopes.Should().NotContain(IdentityServerConstants.StandardScopes.OfflineAccess);
        }

        [Fact]
        public async Task GetAccessTokenClaimsAsync_should_return_standard_user_claims()
        {
            var claims = await _subject.GetAccessTokenClaimsAsync(_user, _client, _resources, _validatedRequest);

            var types = claims.Select(x => x.Type);
            types.Should().Contain(JwtClaimTypes.Subject);
            types.Should().Contain(JwtClaimTypes.AuthenticationTime);
            types.Should().Contain(JwtClaimTypes.IdentityProvider);
            types.Should().Contain(JwtClaimTypes.AuthenticationMethod);
            types.Should().Contain(JwtClaimTypes.AuthenticationContextClassReference);
        }

        [Fact]
        public async Task GetAccessTokenClaimsAsync_should_only_contain_api_claims()
        {
            _resources.IdentityResources.Add(new IdentityResource("id1", new[] { "foo" }));
            _resources.ApiResources.Add(new ApiResource("api1", new string[] { "bar" }));

            var claims = await _subject.GetAccessTokenClaimsAsync(_user, _client, _resources, _validatedRequest);

            _mockMockProfileService.GetProfileWasCalled.Should().BeTrue();
            _mockMockProfileService.ProfileContext.RequestedClaimTypes.Should().NotContain("foo");
            _mockMockProfileService.ProfileContext.RequestedClaimTypes.Should().Contain("bar");
        }

        [Fact]
        public async Task GetAccessTokenClaimsAsync_should_filter_protocol_claims_from_profile_service()
        {
            _resources.ApiResources.Add(new ApiResource("api1", new[] { "foo" }));
            _mockMockProfileService.ProfileClaims.Add(new Claim("aud", "bar"));

            var claims = await _subject.GetAccessTokenClaimsAsync(_user, _client, _resources, _validatedRequest);

            claims.Count(x => x.Type == "aud" && x.Value == "bar").Should().Be(0);
        }

        [Fact]
        public async Task GetAccessTokenClaimsAsync_should_request_api_claims()
        {
            _resources.ApiResources.Add(new ApiResource("api1", new[] { "foo" }));

            var claims = await _subject.GetAccessTokenClaimsAsync(_user, _client, _resources, _validatedRequest);

            _mockMockProfileService.ProfileContext.RequestedClaimTypes.Should().Contain("foo");
        }

        [Fact]
        public async Task GetAccessTokenClaimsAsync_should_request_api_scope_claims()
        {
            _resources.ApiResources.Add(
                new ApiResource("api")
                {
                    Scopes =
                    {
                        new Scope("api1")
                        {
                            UserClaims = { "foo" }
                        }
                    }
                }
            );

            var claims = await _subject.GetAccessTokenClaimsAsync(_user, _client, _resources, _validatedRequest);

            _mockMockProfileService.ProfileContext.RequestedClaimTypes.Should().Contain("foo");
        }

        [Fact]
        public async Task GetAccessTokenClaimsAsync_should_request_both_api_and_api_scope_claims()
        {
            _resources.ApiResources.Add(
                new ApiResource("api")
                {
                    UserClaims = { "foo" },
                    Scopes =
                    {
                        new Scope("api1")
                        {
                            UserClaims = { "bar" }
                        }
                    }
                }
            );

            var claims = await _subject.GetAccessTokenClaimsAsync(_user, _client, _resources, _validatedRequest);

            _mockMockProfileService.ProfileContext.RequestedClaimTypes.Should().Contain("foo");
            _mockMockProfileService.ProfileContext.RequestedClaimTypes.Should().Contain("bar");
        }
    }
}
