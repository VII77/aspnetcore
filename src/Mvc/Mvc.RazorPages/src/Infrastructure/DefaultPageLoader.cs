// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Razor.Compilation;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure
{
    internal class DefaultPageLoader : PageLoader
    {
        private readonly IViewCompilerProvider _viewCompilerProvider;
        private readonly ActionEndpointFactory _endpointFactory;
        private readonly CompiledPageActionDescriptorFactory _compiledPageActionDescriptorFactory;

        public DefaultPageLoader(
            IViewCompilerProvider viewCompilerProvider,
            ActionEndpointFactory endpointFactory,
            CompiledPageActionDescriptorFactory compiledPageActionDescriptorFactory)
        {
            _viewCompilerProvider = viewCompilerProvider;
            _endpointFactory = endpointFactory;
            _compiledPageActionDescriptorFactory = compiledPageActionDescriptorFactory;
        }

        internal IViewCompiler Compiler => _viewCompilerProvider.GetCompiler();

        [Obsolete]
        public override Task<CompiledPageActionDescriptor> LoadAsync(PageActionDescriptor actionDescriptor)
            => LoadAsync(actionDescriptor, EndpointMetadataCollection.Empty);

        public override Task<CompiledPageActionDescriptor> LoadAsync(PageActionDescriptor actionDescriptor, EndpointMetadataCollection endpointMetadata)
        {
            if (actionDescriptor == null)
            {
                throw new ArgumentNullException(nameof(actionDescriptor));
            }

            var task = actionDescriptor.CompiledPageActionDescriptorTask;

            if (task != null)
            {
                return task;
            }

            return actionDescriptor.CompiledPageActionDescriptorTask = LoadAsyncCore(actionDescriptor, endpointMetadata);
        }

        internal async Task<CompiledPageActionDescriptor> LoadWithoutEndpoint(PageActionDescriptor actionDescriptor)
        {
            var viewDescriptor = await Compiler.CompileAsync(actionDescriptor.RelativePath);
            return _compiledPageActionDescriptorFactory.CreateCompiledDescriptor(actionDescriptor, viewDescriptor);
        }

        private async Task<CompiledPageActionDescriptor> LoadAsyncCore(PageActionDescriptor actionDescriptor, EndpointMetadataCollection endpointMetadata)
        {
            var compiled = await LoadWithoutEndpoint(actionDescriptor);

            // We need to create an endpoint for routing to use and attach it to the CompiledPageActionDescriptor...
            // routing for pages is two-phase. First we perform routing using the route info - we can do this without
            // compiling/loading the page. Then once we have a match we load the page and we can create an endpoint
            // with all of the information we get from the compiled action descriptor.
            var endpoints = new List<Endpoint>();
            _endpointFactory.AddEndpoints(
                endpoints,
                routeNames: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                action: compiled,
                routes: Array.Empty<ConventionalRouteEntry>(),
                conventions: new Action<EndpointBuilder>[]
                {
                    b =>
                    {
                        // Metadata from PageActionDescriptor is less significant than the one discovered from the compiled type.
                        // Consequently, we'll insert it at the beginning.
                        for (var i = endpointMetadata.Count - 1; i >=0; i--)
                        {
                            b.Metadata.Insert(0, endpointMetadata[i]);
                        }
                    },
                },
                createInertEndpoints: false);

            // In some test scenarios there's no route so the endpoint isn't created. This is fine because
            // it won't happen for real.
            compiled.Endpoint = endpoints.SingleOrDefault();

            return compiled;
        }
    }
}
