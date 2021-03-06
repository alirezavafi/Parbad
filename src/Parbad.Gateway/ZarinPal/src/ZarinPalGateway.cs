﻿// Copyright (c) Parbad. All rights reserved.
// Licensed under the GNU GENERAL PUBLIC License, Version 3.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Parbad.Abstraction;
using Parbad.Gateway.ZarinPal.Internal;
using Parbad.GatewayBuilders;
using Parbad.Internal;
using Parbad.Net;
using Parbad.Options;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Parbad.Storage.Abstractions.Models;

namespace Parbad.Gateway.ZarinPal
{
    [Gateway(Name)]
    public class ZarinPalGateway : GatewayBase<ZarinPalGatewayAccount>
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly HttpClient _httpClient;
        private readonly ZarinPalGatewayOptions _gatewayOptions;
        private readonly IOptions<MessagesOptions> _messagesOptions;

        public const string Name = "ZarinPal";

        public ZarinPalGateway(
            IGatewayAccountProvider<ZarinPalGatewayAccount> accountProvider,
            IHttpContextAccessor httpContextAccessor,
            IHttpClientFactory httpClientFactory,
            IOptions<ZarinPalGatewayOptions> gatewayOptions,
            IOptions<MessagesOptions> messagesOptions) : base(accountProvider)
        {
            _httpContextAccessor = httpContextAccessor;
            _httpClient = httpClientFactory.CreateClient(this);
            _gatewayOptions = gatewayOptions.Value;
            _messagesOptions = messagesOptions;
        }

        /// <inheritdoc />
        public override async Task<IPaymentRequestResult> RequestAsync(Invoice invoice, CancellationToken cancellationToken = default)
        {
            if (invoice == null) throw new ArgumentNullException(nameof(invoice));

            var account = await GetAccountAsync(invoice).ConfigureAwaitFalse();

            var data = ZarinPalHelper.CreateRequestData(account, invoice);

            var responseMessage = await _httpClient
                .PostXmlAsync(ZarinPalHelper.GetApiUrl(account.IsSandbox, _gatewayOptions), data, cancellationToken)
                .ConfigureAwaitFalse();

            var response = await responseMessage.Content.ReadAsStringAsync().ConfigureAwaitFalse();

            return ZarinPalHelper.CreateRequestResult(response, _httpContextAccessor.HttpContext, account, _gatewayOptions, _messagesOptions.Value);
        }

        public override async Task<IPaymentFetchResult> FetchAsync(InvoiceContext context, CancellationToken cancellationToken = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var callbackResult = await GetCallbackResult(context, cancellationToken);
            if (callbackResult.IsSucceed)
            {
                return PaymentFetchResult.ReadyForVerifying(callbackResult);
            }

            return PaymentFetchResult.Failed(callbackResult, callbackResult.Message);
        }

        private async Task<ZarinPalCallbackResult> GetCallbackResult(InvoiceContext context, CancellationToken cancellationToken)
        {
            var callBackTransaction = context.Transactions.SingleOrDefault(x => x.Type == TransactionType.Callback);

            ZarinPalCallbackResult callbackResult;
            if (callBackTransaction == null)
            {
                callbackResult = await ZarinPalHelper.CreateCallbackResultAsync(_httpContextAccessor.HttpContext.Request, cancellationToken).ConfigureAwaitFalse();
            }
            else
            {
                callbackResult =
                    JsonConvert.DeserializeObject<ZarinPalCallbackResult>(callBackTransaction.AdditionalData);
            }

            return callbackResult;
        }


        /// <inheritdoc />
        public override async Task<IPaymentVerifyResult> VerifyAsync(InvoiceContext context, CancellationToken cancellationToken = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var callbackResult = await GetCallbackResult(context, cancellationToken);

            if (!callbackResult.IsSucceed)
            {
                return PaymentVerifyResult.Failed(callbackResult.Message);
            }

            var account = await GetAccountAsync(context.Payment).ConfigureAwaitFalse();

            var data = ZarinPalHelper.CreateVerifyData(account, callbackResult, context.Payment.Amount);

            var responseMessage = await _httpClient
                .PostXmlAsync(ZarinPalHelper.GetApiUrl(account.IsSandbox, _gatewayOptions), data, cancellationToken)
                .ConfigureAwaitFalse();

            var response = await responseMessage.Content.ReadAsStringAsync().ConfigureAwaitFalse();

            return ZarinPalHelper.CreateVerifyResult(response, _messagesOptions.Value);
        }

        /// <inheritdoc />
        public override Task<IPaymentRefundResult> RefundAsync(InvoiceContext context, Money amount, CancellationToken cancellationToken = default)
        {
            return PaymentRefundResult.Failed("The Refund operation is not supported by this gateway.").ToInterfaceAsync();
        }
    }
}
