// Copyright (c) Parbad. All rights reserved.
// Licensed under the GNU GENERAL PUBLIC License, Version 3.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Parbad.Abstraction;
using Parbad.GatewayBuilders;
using Parbad.Http;
using Parbad.Internal;
using Parbad.Options;

namespace Parbad.Gateway.ParbadVirtual
{
    [Gateway(Name)]
    public class ParbadVirtualGateway : GatewayBase<ParbadVirtualGatewayAccount>
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IOptions<ParbadVirtualGatewayOptions> _options;
        private readonly IOptions<MessagesOptions> _messageOptions;

        public const string Name = "ParbadVirtual";

        public ParbadVirtualGateway(
            IHttpContextAccessor httpContextAccessor,
            IOptions<ParbadVirtualGatewayOptions> options,
            IGatewayAccountProvider<ParbadVirtualGatewayAccount> accountProvider,
            IOptions<MessagesOptions> messageOptions) : base(accountProvider)
        {
            _httpContextAccessor = httpContextAccessor;
            _options = options;
            _messageOptions = messageOptions;
        }

        /// <inheritdoc />
        public override async Task<IPaymentRequestResult> RequestAsync(Invoice invoice, CancellationToken cancellationToken = default)
        {
            var httpContext = _httpContextAccessor.HttpContext;

            var account = await GetAccountAsync(invoice).ConfigureAwaitFalse();

            var url = $"{httpContext.Request.Scheme}" +
                      "://" +
                      $"{httpContext.Request.Host.ToUriComponent()}" +
                      $"{_options.Value.GatewayPath}";

            return PaymentRequestResult.SucceedWithPost(
                account.Name,
                httpContext,
                url,
                new Dictionary<string, string>
                {
                    {"CommandType", "request"},
                    {"trackingNumber", invoice.TrackingNumber.ToString()},
                    {"amount", invoice.Amount.ToLongString()},
                    {"redirectUrl", invoice.CallbackUrl}
                });
        }

        /// <inheritdoc />
        public override async Task<IPaymentFetchResult> FetchAsync(InvoiceContext context, CancellationToken cancellationToken = default)
        {
            var callbackResult = await GetCallbackResult(cancellationToken);

            if (callbackResult.IsSucceed)
            {
                return PaymentFetchResult.ReadyForVerifying(callbackResult);
            }

            return PaymentFetchResult.Failed(callbackResult, callbackResult.Message);
        }

        /// <inheritdoc />
        public override async Task<IPaymentVerifyResult> VerifyAsync(InvoiceContext context, CancellationToken cancellationToken = default)
        {
            var request = _httpContextAccessor.HttpContext.Request;

            var callbackResult = await GetCallbackResult(cancellationToken);

            if (!callbackResult.IsSucceed)
            {
                return PaymentVerifyResult.Failed(callbackResult.Message);
            }

            var transactionCode = await request.TryGetParamAsync("TransactionCode", cancellationToken).ConfigureAwaitFalse();

            return PaymentVerifyResult.Succeed(transactionCode.Value, _messageOptions.Value.PaymentSucceed);
        }

        /// <inheritdoc />
        public override Task<IPaymentRefundResult> RefundAsync(InvoiceContext context, Money amount, CancellationToken cancellationToken = default)
        {
            return PaymentRefundResult.Succeed().ToInterfaceAsync();
        }

        private async Task<(bool IsSucceed, string Message)> GetCallbackResult(CancellationToken cancellationToken)
        {
            var request = _httpContextAccessor.HttpContext.Request;

            var result = await request.TryGetParamAsync("result", cancellationToken);

            var isSucceed = result.Exists && result.Value.Equals("true", StringComparison.OrdinalIgnoreCase);

            var message = isSucceed ? _messageOptions.Value.PaymentSucceed : _messageOptions.Value.PaymentFailed;

            return (isSucceed, message);
        }
    }
}
