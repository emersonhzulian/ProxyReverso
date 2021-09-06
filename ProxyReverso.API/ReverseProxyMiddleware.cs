using Microsoft.AspNetCore.Http;
using ProxyReverso.API;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text.Json;
using System.Threading.Tasks;

namespace ReverseProxyApplication
{
    public class ReverseProxyMiddleware
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly RequestDelegate _nextMiddleware;

        public ReverseProxyMiddleware(RequestDelegate nextMiddleware)
        {
            _nextMiddleware = nextMiddleware;
        }

        public async Task Invoke(HttpContext context)
        {

            context.Request.EnableBuffering();

            var targetUri = BuildTargetUri(context.Request);

            if (targetUri != null)
            {
                var targetRequestMessage = CreateTargetMessage(context, targetUri);

                DadosRetornados dadosRetornados = new DadosRetornados();

                string codigoIdentificadorRequest = GerarCodigoIdentificadorRequest(targetRequestMessage);

                try
                {
                    var responseMessage = await _httpClient.SendAsync(targetRequestMessage, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);

                    dadosRetornados = await RecuperarDadosRetornados(responseMessage);

                    var conteudoRetorndo = new StreamReader(dadosRetornados.Body).ReadToEnd();

                    dadosRetornados.Body.Position = 0;

                    if (dadosRetornados.StatusCode == 200 && 
                        ValidarRetornoValido(conteudoRetorndo))
                    {
                        //faz cache
                        using Stream stream = new FileStream(codigoIdentificadorRequest, FileMode.OpenOrCreate);

                        await JsonSerializer.SerializeAsync(stream, dadosRetornados, new JsonSerializerOptions()
                        {
                            PropertyNamingPolicy = null,
                            WriteIndented = true,
                            AllowTrailingCommas = true,
                            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                        });

                    }
                    else
                    {
                        //busca do cache
                        if (File.Exists(codigoIdentificadorRequest))
                        {
                            using Stream stream = new FileStream(codigoIdentificadorRequest, FileMode.Open, FileAccess.Read);
                            dadosRetornados = await JsonSerializer.DeserializeAsync<DadosRetornados>(stream, new JsonSerializerOptions()
                            {
                                PropertyNamingPolicy = null,
                                WriteIndented = true,
                                AllowTrailingCommas = true,
                                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                            });
                        }

                    }
                }
                catch (Exception ex)
                {
                    dadosRetornados.StatusCode = 500;
                }
                finally
                {
                    await CopyFromDadosRetornados(context, dadosRetornados);
                }

                return;
            }
            await _nextMiddleware(context);
        }

        private bool ValidarRetornoValido(string conteudoRetornado)
        {
            //if(targetRequestMessage.RequestUri)
            //{

            //}

            if (conteudoRetornado.Contains("Valido\":false"))
                return false;
            return true;
        }

        private string GerarCodigoIdentificadorRequest(HttpRequestMessage request)
        {
            return $"{ request.RequestUri.GetHashCode()}-{request.Content?.GetHashCode() ?? 0}.dat";
        }

        private static async Task<DadosRetornados> RecuperarDadosRetornados(HttpResponseMessage responseMessage)
        {
            var dadosRetornados = new DadosRetornados();

            dadosRetornados.StatusCode = (int)responseMessage.StatusCode;

            foreach (var header in responseMessage.Headers)
            {
                dadosRetornados.ResponseHeaders.Add(header.Key, header.Value);
            }

            foreach (var header in responseMessage.Content.Headers)
            {
                dadosRetornados.ResponseContentHeaders.Add(header.Key, header.Value);
            }

            await responseMessage.Content.CopyToAsync(dadosRetornados.Body);
            dadosRetornados.Body.Position = 0;

            return dadosRetornados;
        }

        private HttpRequestMessage CreateTargetMessage(HttpContext context, Uri targetUri)
        {
            var requestMessage = new HttpRequestMessage();
            CopyFromOriginalRequestContentAndHeaders(context, requestMessage);

            requestMessage.RequestUri = targetUri;
            requestMessage.Headers.Host = targetUri.Host;
            requestMessage.Method = GetMethod(context.Request.Method);

            return requestMessage;
        }

        private void CopyFromOriginalRequestContentAndHeaders(HttpContext context, HttpRequestMessage requestMessage)
        {
            var requestMethod = context.Request.Method;

            if (!HttpMethods.IsGet(requestMethod) &&
              !HttpMethods.IsHead(requestMethod) &&
              !HttpMethods.IsDelete(requestMethod) &&
              !HttpMethods.IsTrace(requestMethod))
            {
                context.Request.Body.Seek(0, SeekOrigin.Begin);

                var streamContent = new StreamContent(context.Request.Body);

                requestMessage.Content = new StringContent(streamContent.ReadAsStringAsync().Result, System.Text.Encoding.UTF8, "application/json");
            }

            foreach (var header in context.Request.Headers)
            {
                requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        private async Task CopyFromDadosRetornados(HttpContext context, DadosRetornados dadosRetornados)
        {
            foreach (var header in dadosRetornados.ResponseHeaders)
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }

            foreach (var header in dadosRetornados.ResponseContentHeaders)
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
            context.Response.Headers.Remove("transfer-encoding");

            context.Response.StatusCode = dadosRetornados.StatusCode;

            if (dadosRetornados.Body != null)
                await dadosRetornados.Body.CopyToAsync(context.Response.Body);
        }

        private static HttpMethod GetMethod(string method)
        {
            if (HttpMethods.IsDelete(method)) return HttpMethod.Delete;
            if (HttpMethods.IsGet(method)) return HttpMethod.Get;
            if (HttpMethods.IsHead(method)) return HttpMethod.Head;
            if (HttpMethods.IsOptions(method)) return HttpMethod.Options;
            if (HttpMethods.IsPost(method)) return HttpMethod.Post;
            if (HttpMethods.IsPut(method)) return HttpMethod.Put;
            if (HttpMethods.IsTrace(method)) return HttpMethod.Trace;
            return new HttpMethod(method);
        }

        private Uri BuildTargetUri(HttpRequest request)
        {
            Uri targetUri = null;

            if (request.Path.ToString().StartsWith("/http"))
            {
                var url = request.Path.ToString().Substring(1, request.Path.ToString().Length - 1);
                targetUri = new Uri(url);
            }


            return targetUri;
        }
    }
}