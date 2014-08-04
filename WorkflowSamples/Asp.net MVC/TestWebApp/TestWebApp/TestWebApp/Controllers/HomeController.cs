using System;
using System.Configuration;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using System.Web;
using System.Net;
using System.IO;

using RestSharp;

namespace TestWebApp.Controllers
{
	public class HomeController : Controller
	{
		// NOTE : Bucket name should be all lower case.
		static string _bucketName = " ";   // Bucket name to upload your files

		// Get the client key and client secret from https://developer.api.autodesk.com
		static string _clientKey = ConfigurationManager.AppSettings["CLIENT_ID"]; // Viewing Service client key
		static string _secretKey = ConfigurationManager.AppSettings["CLIENT_SECRET"]; // Viewing service client secret

		static string BASE_URL = ConfigurationManager.AppSettings["BASE_URL"];

		static RestClient _restclient = null;

		static string _accessToken = string.Empty;
		static string _ossfileid = string.Empty;
		static string _ossfileid_encoded = string.Empty;

		// Some document urn to start with. This will be generated at runtime from the uploaded file.
		static string _bubbleurn_encoded = "urn:dXJuOmFkc2sub2JqZWN0czpvcy5vYmplY3Q6YWRuZGV2dGVjaHZpcnUvM2Rzb2xpZC5kd2c=";
		static HttpPostedFileBase _fileToUpload = null;

		static bool _isTokenSet = false;
		static bool _bucketAvailable = false;
		static bool _fileUploadedToOSS = false;
		static bool _fileTranslated = false;
		static string _message = string.Empty;

		public ActionResult Index()
		{
			if (_restclient == null)
				_restclient = new RestClient(BASE_URL);

			GetAccessToken();
			SetAccessToken();
			CreateOSSBucket();

			if (!_bucketAvailable)
			{
				ViewBag.Message = string.Format("Bucket {0} was not created !", _bucketName);
			}
			else if (!_fileUploadedToOSS)
			{
				ViewBag.Message = "Please choose a file and upload to storage.";
			}
			else
			{
				ViewBag.Message = _message;
			}

			ViewBag.AccessToken = _accessToken;
			ViewBag.DocumentId = _bubbleurn_encoded;

			return View();
		}

		// Choose file to upload
		[HttpPost]
		public ActionResult Index(HttpPostedFileBase file)
		{
			_fileToUpload = file;
			_fileUploadedToOSS = false;

			if (_fileToUpload != null)
			{
				string contentType = _fileToUpload.ContentType;
				int contentLen = _fileToUpload.ContentLength;
				UploadFile();

			}
			return RedirectToAction("Index", "Home");
		}

		public ActionResult GetAccessTokenAction()
		{
			GetAccessToken();
			ViewBag.AccessToken = _accessToken;
			return RedirectToAction("Index", "Home");
		}

		public ActionResult SetAccessTokenAction()
		{
			SetAccessToken();
			return RedirectToAction("Index", "Home");
		}

		// Get Access token
		public void GetAccessToken()
		{
			if (_restclient == null)
				_restclient = new RestClient(BASE_URL);

			if (string.IsNullOrEmpty(_accessToken))
			{
				// Get Access Token using clientkey and secretkey
				if (_clientKey != string.Empty && _secretKey != string.Empty)
				{
					RestRequest req = new RestRequest();
					req.Resource = "authentication/v1/authenticate";
					req.Method = Method.POST;
					req.AddHeader("Content-Type", "application/x-www-form-urlencoded");
					req.AddParameter("client_id", _clientKey);
					req.AddParameter("client_secret", _secretKey);
					req.AddParameter("grant_type", "client_credentials");

					IRestResponse<TestWebApp.Models.AuthResult> resp = _restclient.Execute<TestWebApp.Models.AuthResult>(req);
					if (resp.StatusCode == System.Net.HttpStatusCode.OK)
					{
						TestWebApp.Models.AuthResult ar = resp.Data;
						if (ar != null)
						{
							_accessToken = ar.access_token;
						}
					}
				}
			}
		}

		// Set Access token
		public void SetAccessToken()
		{
			if (_isTokenSet == false)
			{
				// Set Accesstoken for cookie to be sent automatically in subsequest rest calls
				if (!string.IsNullOrEmpty(_accessToken))
				{
					RestRequest req = new RestRequest();
					req.Resource = "utility/v1/settoken";
					req.Method = Method.POST;
					req.AddHeader("Content-Type", "application/x-www-form-urlencoded");
					req.AddParameter("access-token", _accessToken);

					IRestResponse resp = _restclient.Execute(req);
					if (resp.StatusCode == System.Net.HttpStatusCode.OK)
					{
						_isTokenSet = true;
						string response = resp.Content;
					}
				}
			}
		}

		// Create OSS Bucket
		public void CreateOSSBucket()
		{
			if (_bucketAvailable == false)
			{
				if (_isTokenSet)
				{
					// Create OSS Bucket
					RestRequest req = new RestRequest();
					req.Resource = "oss/v1/buckets";
					req.Method = Method.POST;
					req.AddParameter("Authorization", "Bearer " + _accessToken, ParameterType.HttpHeader);
					req.AddParameter("Content-Type", "application/json", ParameterType.HttpHeader);
					string body = "{\"bucketKey\":\"" + _bucketName + "\",\"servicesAllowed\":{},\"policy\":\"transient\"}";
					req.AddParameter("application/json", body, ParameterType.RequestBody);

					IRestResponse resp = _restclient.Execute(req);

					string response = resp.Content;
					if (resp.StatusCode == System.Net.HttpStatusCode.OK)
					{// Bucket created
						_bucketAvailable = true;
					}
					else if (resp.StatusCode == System.Net.HttpStatusCode.Conflict)
					{
						// already existed
						_bucketAvailable = true;
					}
				}
			}
		}

		public ActionResult TranslateUploadedFileAction()
		{
			TranslateUploadedFile();
			return RedirectToAction("Index", "Home");
		}

		// Upload the file to OSS bucket
		public void UploadFile()
		{
			if (_fileToUpload != null)
			{
				if (_bucketAvailable)
				{
					string objectKey = HttpUtility.UrlEncode(_fileToUpload.FileName);
					byte[] fileData = null;
					int contentLen = _fileToUpload.ContentLength;
					string contentType = _fileToUpload.ContentType;
					using (BinaryReader binaryReader = new BinaryReader(_fileToUpload.InputStream))
					{
						fileData = binaryReader.ReadBytes(contentLen);
					}

					RestRequest req = new RestRequest();
					req.Resource = "oss/v1/buckets/" + _bucketName + "/objects/" + objectKey;
					req.Method = Method.PUT;
					req.AddParameter("Authorization", "Bearer " + _accessToken, ParameterType.HttpHeader);
					req.AddParameter("Content-Type", contentType);
					req.AddParameter("Content-Length", contentLen);
					req.AddParameter("requestBody", fileData, ParameterType.RequestBody);

					IRestResponse resp = _restclient.Execute(req);
					if (resp.StatusCode == System.Net.HttpStatusCode.OK)
					{
						_fileUploadedToOSS = true;
						_message = string.Format("File {0} was uploaded to OSS Bucket named {1}", _fileToUpload.FileName, _bucketName);

						string response = resp.Content;

						_ossfileid = GetIdValueInJson(response);
						_ossfileid_encoded = Base64Encode(_ossfileid);

						TranslateUploadedFile();
					}
					else
						_message = string.Format("Error uploading {0} to {1} bucket !", _fileToUpload.FileName, _bucketName);
				}
				else
				{
					_message = string.Format("OSS Bucket {0} was not found !", _bucketName);
				}
			}
		}

		// Translate the uploaded file
		public void TranslateUploadedFile()
		{
			if (_fileUploadedToOSS)
			{
				RestRequest req = new RestRequest();
				req.Resource = "viewingservice/v1/bubbles";
				req.Method = Method.POST;
				req.AddParameter("Authorization", "Bearer " + _accessToken, ParameterType.HttpHeader);
				req.AddParameter("Content-Type", "application/json;charset=utf-8", ParameterType.HttpHeader);
				string body = "{\"urn\":\"" + _ossfileid_encoded + "\"}";
				req.AddParameter("application/json", body, ParameterType.RequestBody);

				IRestResponse resp = _restclient.Execute(req);
				string respText = resp.ToString();

				string response = resp.Content;
				if (resp.StatusCode == System.Net.HttpStatusCode.OK)
				{
					// Translation started. May not be complete yet.
					_message = string.Format("Translation started. Please wait for the translation to complete.");
				}
				else if (resp.StatusCode == System.Net.HttpStatusCode.Created)
				{
					// Translation is done.
					_fileTranslated = true;

					_bubbleurn_encoded = string.Format("urn:{0}", _ossfileid_encoded);

					_message = string.Format("Document URN has been updated. Please click on Load to update the viewer model.");
				}
			}
		}

		public ActionResult About()
		{
			return View();
		}

		//Helper methods
		public static string Base64Decode(string base64EncodedData)
		{
			byte[] bytes = Convert.FromBase64String(base64EncodedData);
			return System.Text.Encoding.UTF8.GetString(bytes);
		}

		public static string Base64Encode(string plainText)
		{
			byte[] bytes = System.Text.Encoding.UTF8.GetBytes(plainText);
			return Convert.ToBase64String(bytes);
		}

		private static string GetIdValueInJson(string content)
		{
			string idSrcFlag = "\"id\" : \"";
			int index = content.IndexOf(idSrcFlag) + idSrcFlag.Length;
			int idLen = content.IndexOf("\"", index + 1) - index;
			var urn = content.Substring(index, idLen);
			return urn;
		}
	}
}