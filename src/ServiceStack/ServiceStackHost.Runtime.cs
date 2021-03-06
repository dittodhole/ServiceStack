﻿// Copyright (c) Service Stack LLC. All Rights Reserved.
// License: https://raw.github.com/ServiceStack/ServiceStack/master/license.txt


using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Web;
using System.Xml;
using ServiceStack.Auth;
using ServiceStack.DataAnnotations;
using ServiceStack.FluentValidation;
using ServiceStack.Host;
using ServiceStack.Host.Handlers;
using ServiceStack.Metadata;
using ServiceStack.MiniProfiler;
using ServiceStack.Support.WebHost;
using ServiceStack.Web;

namespace ServiceStack
{
    public abstract partial class ServiceStackHost
    {
        public virtual object ApplyRequestConverters(IRequest req, object requestDto)
        {
            foreach (var converter in RequestConverters)
            {
                requestDto = converter(req, requestDto) ?? requestDto;
                if (req.Response.IsClosed)
                    return requestDto;
            }

            return requestDto;
        }

        public virtual object ApplyResponseConverters(IRequest req, object responseDto)
        {
            foreach (var converter in ResponseConverters)
            {
                responseDto = converter(req, responseDto) ?? responseDto;
                if (req.Response.IsClosed)
                    return responseDto;
            }

            return responseDto;
        }

        /// <summary>
        /// Apply PreRequest Filters for participating Custom Handlers, e.g. RazorFormat, MarkdownFormat, etc
        /// </summary>
        public virtual bool ApplyCustomHandlerRequestFilters(IRequest httpReq, IResponse httpRes)
        {
            return ApplyPreRequestFilters(httpReq, httpRes);
        }

        /// <summary>
        /// Applies the raw request filters. Returns whether or not the request has been handled 
        /// and no more processing should be done.
        /// </summary>
        /// <returns></returns>
        public virtual bool ApplyPreRequestFilters(IRequest httpReq, IResponse httpRes)
        {
            if (PreRequestFilters.Count == 0)
                return false;

            using (Profiler.Current.Step("Executing Pre RequestFilters"))
            {
                foreach (var requestFilter in PreRequestFilters)
                {
                    requestFilter(httpReq, httpRes);
                    if (httpRes.IsClosed) break;
                }

                return httpRes.IsClosed;
            }
        }

        /// <summary>
        /// Applies the request filters. Returns whether or not the request has been handled 
        /// and no more processing should be done.
        /// </summary>
        /// <returns></returns>
        public virtual bool ApplyRequestFilters(IRequest req, IResponse res, object requestDto)
        {
            req.ThrowIfNull("req");
            res.ThrowIfNull("res");

            if (res.IsClosed)
                return true;

            using (Profiler.Current.Step("Executing Request Filters"))
            {
                if (!req.IsMultiRequest())
                    return ApplyRequestFiltersSingle(req, res, requestDto);

                var dtos = (IEnumerable)requestDto;
                foreach (var dto in dtos)
                {
                    if (ApplyRequestFiltersSingle(req, res, dto))
                        return true;
                }
                return false;
            }
        }

        protected virtual bool ApplyRequestFiltersSingle(IRequest req, IResponse res, object requestDto)
        {
            //Exec all RequestFilter attributes with Priority < 0
            var attributes = FilterAttributeCache.GetRequestFilterAttributes(requestDto.GetType());
            var i = 0;
            for (; i < attributes.Length && attributes[i].Priority < 0; i++)
            {
                var attribute = attributes[i];
                Container.AutoWire(attribute);
                attribute.RequestFilter(req, res, requestDto);
                Release(attribute);
                if (res.IsClosed) return res.IsClosed;
            }

            ExecTypedFilters(GlobalTypedRequestFilters, req, res, requestDto);
            if (res.IsClosed) return res.IsClosed;

            //Exec global filters
            foreach (var requestFilter in GlobalRequestFilters)
            {
                requestFilter(req, res, requestDto);
                if (res.IsClosed) return res.IsClosed;
            }

            //Exec remaining RequestFilter attributes with Priority >= 0
            for (; i < attributes.Length && attributes[i].Priority >= 0; i++)
            {
                var attribute = attributes[i];
                Container.AutoWire(attribute);
                attribute.RequestFilter(req, res, requestDto);
                Release(attribute);
                if (res.IsClosed) return res.IsClosed;
            }

            return res.IsClosed;
        }

        /// <summary>
        /// Applies the response filters. Returns whether or not the request has been handled 
        /// and no more processing should be done.
        /// </summary>
        /// <returns></returns>
        public virtual bool ApplyResponseFilters(IRequest req, IResponse res, object response)
        {
            req.ThrowIfNull("req");
            res.ThrowIfNull("res");

            if (res.IsClosed)
                return true;

            using (Profiler.Current.Step("Executing Response Filters"))
            {
                if (!req.IsMultiRequest() || !(response is IEnumerable))
                    return ApplyResponseFiltersSingle(req, res, response);

                var dtos = (IEnumerable)response;
                foreach (var dto in dtos)
                {
                    if (ApplyResponseFiltersSingle(req, res, dto))
                        return true;
                }
                return false;
            }
        }

        protected virtual bool ApplyResponseFiltersSingle(IRequest req, IResponse res, object response)
        {
            var responseDto = response.GetResponseDto();
            var attributes = responseDto != null
                ? FilterAttributeCache.GetResponseFilterAttributes(responseDto.GetType())
                : null;

            //Exec all ResponseFilter attributes with Priority < 0
            var i = 0;
            if (attributes != null)
            {
                for (; i < attributes.Length && attributes[i].Priority < 0; i++)
                {
                    var attribute = attributes[i];
                    Container.AutoWire(attribute);
                    attribute.ResponseFilter(req, res, response);
                    Release(attribute);
                    if (res.IsClosed) return res.IsClosed;
                }
            }

            ExecTypedFilters(GlobalTypedResponseFilters, req, res, response);
            if (res.IsClosed) return res.IsClosed;

            //Exec global filters
            foreach (var responseFilter in GlobalResponseFilters)
            {
                responseFilter(req, res, response);
                if (res.IsClosed) return res.IsClosed;
            }

            //Exec remaining RequestFilter attributes with Priority >= 0
            if (attributes != null)
            {
                for (; i < attributes.Length; i++)
                {
                    var attribute = attributes[i];
                    Container.AutoWire(attribute);
                    attribute.ResponseFilter(req, res, response);
                    Release(attribute);
                    if (res.IsClosed) return res.IsClosed;
                }
            }

            return res.IsClosed;
        }

        public virtual bool ApplyMessageRequestFilters(IRequest req, IResponse res, object requestDto)
        {
            ExecTypedFilters(GlobalTypedMessageRequestFilters, req, res, requestDto);
            if (res.IsClosed) return res.IsClosed;

            //Exec global filters
            foreach (var requestFilter in GlobalMessageRequestFilters)
            {
                requestFilter(req, res, requestDto);
                if (res.IsClosed) return res.IsClosed;
            }

            return res.IsClosed;
        }

        public virtual bool ApplyMessageResponseFilters(IRequest req, IResponse res, object response)
        {
            ExecTypedFilters(GlobalTypedMessageResponseFilters, req, res, response);
            if (res.IsClosed) return res.IsClosed;

            //Exec global filters
            foreach (var responseFilter in GlobalMessageResponseFilters)
            {
                responseFilter(req, res, response);
                if (res.IsClosed) return res.IsClosed;
            }

            return res.IsClosed;
        }

        public virtual void ExecTypedFilters(Dictionary<Type, ITypedFilter> typedFilters,
            IRequest req, IResponse res, object dto)
        {
            if (typedFilters.Count == 0) return;

            ITypedFilter typedFilter;
            var dtoType = dto.GetType();
            typedFilters.TryGetValue(dtoType, out typedFilter);
            if (typedFilter != null)
            {
                typedFilter.Invoke(req, res, dto);
                if (res.IsClosed) return;
            }

            var dtoInterfaces = dtoType.GetInterfaces();
            foreach (var dtoInterface in dtoInterfaces)
            {
                typedFilters.TryGetValue(dtoInterface, out typedFilter);
                if (typedFilter != null)
                {
                    typedFilter.Invoke(req, res, dto);
                    if (res.IsClosed) return;
                }
            }
        }

        public MetadataPagesConfig MetadataPagesConfig
        {
            get
            {
                return new MetadataPagesConfig(
                    Metadata,
                    Config.ServiceEndpointsMetadataConfig,
                    Config.IgnoreFormatsInMetadata,
                    ContentTypes.ContentTypeFormats.Keys.ToList());
            }
        }

        public virtual TimeSpan GetDefaultSessionExpiry(IRequest req)
        {
            var sessionFeature = this.GetPlugin<SessionFeature>();
            if (sessionFeature != null)
            {
                return req.IsPermanentSession()
                    ? sessionFeature.PermanentSessionExpiry ?? SessionFeature.DefaultPermanentSessionExpiry
                    : sessionFeature.SessionExpiry ?? SessionFeature.DefaultSessionExpiry;
            }

            return req.IsPermanentSession()
                ? SessionFeature.DefaultPermanentSessionExpiry
                : SessionFeature.DefaultSessionExpiry;
        }

        public bool HasFeature(Feature feature)
        {
            return (feature & Config.EnableFeatures) == feature;
        }

        public void AssertFeatures(Feature usesFeatures)
        {
            if (Config.EnableFeatures == Feature.All) return;

            if (!HasFeature(usesFeatures))
            {
                throw new UnauthorizedAccessException(
                    String.Format("'{0}' Features have been disabled by your administrator", usesFeatures));
            }
        }

        public void AssertContentType(string contentType)
        {
            if (Config.EnableFeatures == Feature.All) return;

            AssertFeatures(contentType.ToFeature());
        }

        public bool HasAccessToMetadata(IRequest httpReq, IResponse httpRes)
        {
            if (!HasFeature(Feature.Metadata))
            {
                HandleErrorResponse(httpReq, httpRes, HttpStatusCode.Forbidden, "Metadata Not Available");
                return false;
            }

            if (Config.MetadataVisibility != RequestAttributes.Any)
            {
                var actualAttributes = httpReq.GetAttributes();
                if ((actualAttributes & Config.MetadataVisibility) != Config.MetadataVisibility)
                {
                    HandleErrorResponse(httpReq, httpRes, HttpStatusCode.Forbidden, "Metadata Not Visible");
                    return false;
                }
            }
            return true;
        }

        public void HandleErrorResponse(IRequest httpReq, IResponse httpRes, HttpStatusCode errorStatus, string errorStatusDescription = null)
        {
            if (httpRes.IsClosed) return;

            httpRes.StatusDescription = errorStatusDescription;

            var handler = GetCustomErrorHandler(errorStatus)
                ?? GlobalHtmlErrorHttpHandler
                ?? GetNotFoundHandler();

            handler.ProcessRequest(httpReq, httpRes, httpReq.OperationName);
        }

        public IServiceStackHandler GetCustomErrorHandler(int errorStatusCode)
        {
            try
            {
                return GetCustomErrorHandler((HttpStatusCode)errorStatusCode);
            }
            catch
            {
                return null;
            }
        }

        public IServiceStackHandler GetCustomErrorHandler(HttpStatusCode errorStatus)
        {
            IServiceStackHandler httpHandler = null;
            if (CustomErrorHttpHandlers != null)
            {
                CustomErrorHttpHandlers.TryGetValue(errorStatus, out httpHandler);
            }

            return httpHandler;
        }

        public IServiceStackHandler GetNotFoundHandler()
        {
            IServiceStackHandler httpHandler = null;
            if (CustomErrorHttpHandlers != null)
            {
                CustomErrorHttpHandlers.TryGetValue(HttpStatusCode.NotFound, out httpHandler);
            }

            return httpHandler ?? new NotFoundHttpHandler();
        }

        public IHttpHandler GetCustomErrorHttpHandler(HttpStatusCode errorStatus)
        {
            var ssHandler = GetCustomErrorHandler(errorStatus)
                ?? GetNotFoundHandler();
            if (ssHandler == null) return null;
            var httpHandler = ssHandler as IHttpHandler;
            return httpHandler ?? new ServiceStackHttpHandler(ssHandler);
        }

        public bool HasValidAuthSecret(IRequest httpReq)
        {
            if (Config.AdminAuthSecret != null)
            {
                var authSecret = httpReq.GetParam(Keywords.AuthSecret);
                return authSecret == Config.AdminAuthSecret;
            }

            return false;
        }

        public virtual void OnExceptionTypeFilter(Exception ex, ResponseStatus responseStatus)
        {
            var argEx = ex as ArgumentException;
            var isValidationSummaryEx = argEx is ValidationException;
            if (argEx != null && !isValidationSummaryEx && argEx.ParamName != null)
            {
                var paramMsgIndex = argEx.Message.LastIndexOf("Parameter name:");
                var errorMsg = paramMsgIndex > 0
                    ? argEx.Message.Substring(0, paramMsgIndex)
                    : argEx.Message;

                responseStatus.Errors.Add(new ResponseError
                {
                    ErrorCode = ex.GetType().Name,
                    FieldName = argEx.ParamName,
                    Message = errorMsg,
                });
            }
        }

        public virtual void OnSaveSession(IRequest httpReq, IAuthSession session, TimeSpan? expiresIn = null)
        {
            if (httpReq == null) return;

            using (var cache = this.GetCacheClient())
            {
                var sessionKey = SessionFeature.GetSessionKey(session.Id ?? httpReq.GetOrCreateSessionId());
                session.LastModified = DateTime.UtcNow;
                cache.CacheSet(sessionKey, session, expiresIn ?? GetDefaultSessionExpiry(httpReq));
            }

            httpReq.Items[SessionFeature.RequestItemsSessionKey] = session;
        }

        public virtual IRequest TryGetCurrentRequest()
        {
            return null;
        }

        public virtual object OnAfterExecute(IRequest req, object requestDto, object response)
        {
            return req.Response.Dto = response;
        }

        public virtual MetadataTypesConfig GetTypesConfigForMetadata(IRequest req)
        {
            var typesConfig = new NativeTypesFeature().MetadataTypesConfig;
            typesConfig.IgnoreTypesInNamespaces.Clear();
            typesConfig.IgnoreTypes.Add(typeof(ResponseStatus));
            typesConfig.IgnoreTypes.Add(typeof(ResponseError));
            return typesConfig;
        }

        public virtual List<Type> ExportSoapOperationTypes(List<Type> operationTypes)
        {
            var types = operationTypes
                .Where(x => !x.AllAttributes<ExcludeAttribute>()
                            .Any(attr => attr.Feature.HasFlag(Feature.Soap)))
                .Where(x => !x.IsGenericTypeDefinition())
                .ToList();
            return types;
        }

        public virtual bool ExportSoapType(Type type)
        {
            return !type.IsGenericTypeDefinition() && 
                   !type.AllAttributes<ExcludeAttribute>()
                        .Any(attr => attr.Feature.HasFlag(Feature.Soap));
        }

        public virtual void WriteSoapMessage(IRequest req, System.ServiceModel.Channels.Message message, Stream outputStream)
        {
            try
            {
                using (var writer = XmlWriter.Create(outputStream, Config.XmlWriterSettings))
                {
                    message.WriteMessage(writer);
                }
            }
            catch (Exception ex)
            {
                var response = OnServiceException(req, req.Dto, ex);
                if (response == null || !outputStream.CanSeek)
                    return;

                outputStream.Position = 0;
                try
                {
                    message = SoapHandler.CreateResponseMessage(response, message.Version, req.Dto.GetType(),
                        req.GetSoapMessage().Headers.Action == null);
                    using (var writer = XmlWriter.Create(outputStream, Config.XmlWriterSettings))
                    {
                        message.WriteMessage(writer);
                    }
                }
                catch { }
            }
            finally
            {
                HostContext.CompleteRequest(req);
            }
        }
    }

}