﻿using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MoneyMaker.Data;
using MoneyMaker.Models;
using MoneyMaker.Services;

namespace MoneyMaker.Controllers;

[EnableCors("Policy")]
[Route("api")]
[ApiController]
public class ApiController : ControllerBase
{
    private readonly ILogger<HomeController> _logger;
    private ApiService apiService;
    private CurrencyService currencyService;
    private AlertService alertService;
    private PortfolioService portfolioService;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly SignInManager<IdentityUser> _signInManager;
    private string LOCAL_CURRENCY = "CAD";

    public ApiController(
        ILogger<HomeController> logger,
        ApplicationDbContext context,
        IHttpClientFactory httpClientFactory,
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager
        )
    {
        _logger = logger;
        apiService = new ApiService(httpClientFactory);
        currencyService = new CurrencyService(context);
        alertService = new AlertService(context);
        portfolioService = new PortfolioService(context);
        _userManager = userManager;
        _signInManager = signInManager;
    }

    // GET api/currencies
    [HttpGet("currencies")]
    [AllowAnonymous]
    public async Task<IEnumerable<Currency>> getCurrencies()
    {
        return await currencyService.GetCurrencies();
    }

    // GET api/convert?from=&to=
    [HttpGet("convert")]
    [AllowAnonymous]
    public async Task<Dictionary<string, string>> getConvert(
        [FromQuery(Name = "from")] string fromCurrency,
        [FromQuery(Name = "to")] string toCurrency)
    {
        Dictionary<string, string> conversionResult = new Dictionary<string, string>();
        float data = await apiService.GetRate(fromCurrency, toCurrency);

        conversionResult.Add("data", data.ToString());
        conversionResult.Add("from", fromCurrency);
        conversionResult.Add("to", toCurrency);

        return conversionResult;
    }

    // GET api/chart?from=&to=
    [HttpGet("chart")]
    [AllowAnonymous]
    public async Task<Dictionary<string, float>> getChart(
        [FromQuery(Name = "from")] string fromCurrency,
        [FromQuery(Name = "to")] string toCurrency)
    {
        return await apiService.GetMonthRate(fromCurrency, toCurrency);
    }


    //=====================================
    // Alert
    //=====================================
    // GET api/alert
    [HttpGet("alert")]
    [AllowAnonymous]
    public async Task<ApiResponse> getUserAlert([FromQuery] string Token, 
        [FromQuery] string FromCurrency,
        [FromQuery] string ToCurrency)
    {
        ApiResponse response = new ApiResponse();
        var searchedUser = await getUserIdWithToken(Token);

        if(searchedUser==null){
            response.Code = "400";
            response.Data.Add("message", "Invalid token");
            return response;
        }

        string userid = searchedUser.Id;
        var user = await _userManager.FindByIdAsync(userid);

        var fromCurr = await currencyService.GetCurrency(FromCurrency);
        var toCurr = await currencyService.GetCurrency(ToCurrency);

        if (user != null && userid != null && userid.Length > 0
            && fromCurr != null && toCurr != null)
        {
            response.Code = "200";
            var alert = await alertService.GetAlert(userid, fromCurr.CurrencySym, toCurr.CurrencySym);
            if(alert != null){
                response.Data.Add("alert", alert);
                return response;
            }else{
                response.Code = "400";
                response.Data.Add("message", "Alert not found");
                return response;
            }
        }else{
            response.Code = "400";
            response.Data.Add("message", "Invalid query");
            return response;
        }
    }


    // GET api/alerts
    [HttpGet("alerts")]
    [AllowAnonymous]
    public async Task<ApiResponse> getUserAlerts([FromQuery] string Token)
    {
        ApiResponse response = new ApiResponse();

        var searchedUser = await getUserIdWithToken(Token);

        if(searchedUser==null)
        {
            response.Code = "400";
            response.Data.Add("message", "Invalid token");
            return response;
        }

        string userid = searchedUser.Id;
        var user = await _userManager.FindByIdAsync(userid);

        if (user != null && userid != null && userid.Length > 0)
        {
            response.Code = "200";
            response.Data.Add("alert", await alertService.GetUserAlerts(userid));
            return response;
        }else{
            response.Code = "400";
            response.Data.Add("message", "Invalid query");
            return response;
        }
        
    }

    // POST api/alert
    [HttpPost("alert")]
    [AllowAnonymous]
    public async Task<ApiResponse> postUserAlert(Alert request, [FromQuery] string Token)
    {
        ApiResponse response = new ApiResponse();
        var user = await getUserIdWithToken(Token);
        if(user==null)
        {
            response.Code = "400";
            response.Data.Add("message", "Invalid token");
            return response;
        }


        Alert createAlert = new Alert();

        createAlert.UserId = user.Id;
        createAlert.AlertName = request.AlertName;
        createAlert.ConditionValue = (float)Math.Round(request.ConditionValue, 2);
        createAlert.CreateDate = DateTime.Today;
        createAlert.FromCurrency = request.FromCurrency;
        createAlert.ToCurrency = request.ToCurrency;
        createAlert.isBelow = request.isBelow;

        bool ex = await alertService.AlertExists(createAlert.UserId, createAlert.FromCurrency, createAlert.ToCurrency);

        if (ex)
        {
            response.Code = "400";
            response.Data.Add("message", "Alert for this conversion already exists, try editing");
            return response;
        }
        else
        {
            await alertService.PostAlert(createAlert);
            response.Code = "200";
            response.Data.Add("message", "Success");
            return response;
        }
    }

    // PUT api/alert
    [HttpPut("alert")]
    [AllowAnonymous]
    public async Task<ApiResponse> putUserAlert(Alert request, [FromQuery] string Token)
    {

        ApiResponse response = new ApiResponse();
        var user = await getUserIdWithToken(Token);
        if (user is null)
        {
            response.Code = "400";
            response.Data.Add("message", "Invalid token");
            return response;
        }

        Alert editAlert = new Alert();
        editAlert.UserId = user.Id;
        editAlert.AlertName = request.AlertName;
        editAlert.ConditionValue = (float)Math.Round(request.ConditionValue, 2) < 0? 0 : (float)Math.Round(request.ConditionValue, 2);
        editAlert.CreateDate = DateTime.Today;
        editAlert.FromCurrency = request.FromCurrency;
        editAlert.ToCurrency = request.ToCurrency;
        editAlert.isBelow = request.isBelow;

        bool ex = await alertService.AlertExists(editAlert.UserId, editAlert.FromCurrency, editAlert.ToCurrency);

        if (ex)
        {
            await alertService.PutAlert(editAlert);
            response.Code = "200";
            response.Data.Add("message", "Success");
            return response;
        }
        else
        {
            response.Code = "400";
            response.Data.Add("message", "Alert for this conversion does not exists, try making one");
            return response;
        }
    }

    // DELETE api/alert
    [HttpDelete("alert")]
    [AllowAnonymous]
    public async Task<ApiResponse> deleteUserAlert(Alert request, [FromQuery] string Token)
    {
        ApiResponse response = new ApiResponse();
        var user = await getUserIdWithToken(Token);

        if (user is null)
        {
            response.Code = "400";
            response.Data.Add("message", "Invalid token");
            return response;
        }
        
        Alert delAlert = new Alert();
        delAlert.UserId = user.Id;
        delAlert.AlertName = request.AlertName;
        delAlert.ConditionValue = (float)Math.Round(request.ConditionValue, 2);
        delAlert.CreateDate = DateTime.Today;
        delAlert.FromCurrency = request.FromCurrency;
        delAlert.ToCurrency = request.ToCurrency;
        delAlert.isBelow = request.isBelow;

        var findAlert = await alertService.GetAlert(delAlert.UserId, delAlert.FromCurrency, delAlert.ToCurrency);
        

        if(findAlert !=null )
        {
            var successawait = await alertService.DeleteAlert(delAlert.UserId, delAlert.FromCurrency, delAlert.ToCurrency);

            response.Code = "200";
            response.Data.Add("deleted", successawait);
            return response;
        }else{
            response.Code = "400";
            response.Data.Add("message", "Not found");
            return response;
        }
    }





    //=====================================
    // Portfolio
    //=====================================
    // GET api/portfolio
    [HttpGet("portfolio")]
    [AllowAnonymous]
    public async Task<ApiResponse> getUserPortfolio(UserApiRequest request)
    {
        ApiResponse response = new ApiResponse();

        string userid = request.Token;
        var user = await _userManager.FindByIdAsync(userid);

        if (user != null && userid.Length > 0)
        {
            response.Code = "200";
            response.Data.Add("portfolio", await portfolioService.GetUserPortfolio(userid));
            return response;
        }
        else
        {
            response.Code = "400";
            response.Data.Add("message", "invalid userid / not logged in");
            return response;
        }
    }

    // GET api/portfolio/sum
    [HttpGet("portfolio/sum")]
    [AllowAnonymous]
    public async Task<ApiResponse> getUserPortfolioSum(UserApiRequest request)
    {
        ApiResponse response = new ApiResponse();

        string userid = request.Token;
        var user = await _userManager.FindByIdAsync(userid);

        if (user != null && userid.Length > 0)
        {
            response.Code = "200";

            float sum = 0;
            var portfolios = await portfolioService.GetUserPortfolio(userid);
            List<PortfolioEntry> portfolioEntries = portfolios.ToList();

            foreach (var portfolioEntry in portfolioEntries)
            {
                var rate = await apiService.GetRate(portfolioEntry.EntryCurrencySym, LOCAL_CURRENCY);
                float subTotal = rate * portfolioEntry.EntryValue;

                sum += subTotal;
            }

            response.Data.Add("sum", sum.ToString());
            return response;
        }
        else
        {
            response.Code = "400";
            response.Data.Add("message", "invalid userid / not logged in");
            return response;
        }
    }

    // POST api/portfolio
    [HttpPost("portfolio")]
    [AllowAnonymous]
    public async Task<ApiResponse> postUserPortfolio(PortfolioEntry port)
    {
        ApiResponse response = new ApiResponse();

        var userid = port.UserId;
        var user = await _userManager.FindByIdAsync(userid);

        if (user is not null)
        {
            await portfolioService.PostPortfolio(port);
            response.Code = "200";
            response.Data.Add("message", "created");
            return response;
        }
        else
        {
            response.Code = "400";
            response.Data.Add("message", "invalid userid / not logged in");
            return response;
        }
    }

    // PUT api/portfolio
    [HttpPut("portfolio")]
    [AllowAnonymous]
    public async Task<ApiResponse> putUserPortfolio(PortfolioEntry port)
    {
        ApiResponse response = new ApiResponse();

        var userid = port.UserId;
        var user = await _userManager.FindByIdAsync(userid);

        if (user is not null)
        {
            await portfolioService.PutPortfolio(port);
            response.Code = "200";
            response.Data.Add("message", "updated");
            return response;
        }
        else
        {
            response.Code = "400";
            response.Data.Add("message", "invalid userid / not logged in");
            return response;
        }
    }

    // DELETE api/portfolio
    [HttpDelete("portfolio")]
    [AllowAnonymous]
    public async Task<ApiResponse> deleteUserPortfolio(PortfolioEntry port)
    {
        ApiResponse response = new ApiResponse();

        var userid = port.UserId;
        var user = await _userManager.FindByIdAsync(userid);

        if (user is not null)
        {
            bool deleted = await portfolioService.DeletePortfolio(port.UserId, port.EntryCurrencySym);

            if (deleted)
            {
                response.Code = "200";
                response.Data.Add("message", "deleted");
                return response;
            }
            else
            {
                response.Code = "400";
                response.Data.Add("message", "fail to delete");
                return response;
            }
        }
        else
        {
            response.Code = "400";
            response.Data.Add("message", "invalid userid / not logged in");
            return response;
        }
    }

    //=====================================
    // Login & Register
    //=====================================
    // POST api/login
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ApiResponse> postLogin(SignInCredential cred)
    {

        ApiResponse response = new ApiResponse();

        var user = _userManager.Users.SingleOrDefault(u => u.UserName == cred.Email);
        
        if (user is null)
        {
            response.Code = "404";
            response.Data.Add("message", "User not found");
            return response;
        }

        var result = await _signInManager.PasswordSignInAsync(cred.Email, cred.Password, false, lockoutOnFailure: true);
                
        if (result.Succeeded)
        {
            var provider = MD5.Create();
            string salt = user.Email;
            string password = user.PasswordHash;
            byte[] bytes = provider.ComputeHash(Encoding.ASCII.GetBytes(salt + password));
            string computedHash = BitConverter.ToString(bytes);

            var hashText = System.Text.Encoding.UTF8.GetBytes(computedHash);
            var token = System.Convert.ToBase64String(hashText);

            response.Code = "200";
            response.Data.Add("message", "Logged in");
            response.Data.Add("userid", user.Id);
            response.Data.Add("token" , token);
            return response;
        }
        if (result.RequiresTwoFactor)
        {
            response.Code = "400";
            response.Data.Add("message", "Require 2-factor");
            return response;
        }
        response.Code = "400";
        response.Data.Add("message", "Failed login");
        return response;
    }

    private async Task<IdentityUser> getUserIdWithToken(string token){
        List<IdentityUser> userlist = await _userManager.Users.ToListAsync();
        foreach (var user in userlist)
        {
            var provider = MD5.Create();
            string salt = user.Email;
            string password = user.PasswordHash;
            byte[] bytes = provider.ComputeHash(Encoding.ASCII.GetBytes(salt + password));
            string computedHash = BitConverter.ToString(bytes);

            var hashText = System.Text.Encoding.UTF8.GetBytes(computedHash);
            var userToken = System.Convert.ToBase64String(hashText);

            if(userToken.Equals(token)){
                return user;
            }
        }

        return null;
    }

    // get api/isLogin
    [HttpGet("isLogin")]
    [AllowAnonymous]
    public async Task<ApiResponse> getIsLogin()
    {
        ApiResponse response = new ApiResponse();
        string userid = _userManager.GetUserId(User);

        response.Code = "200";

        if (userid == null)
        {
            response.Data.Add("login", false);
        }
        else
        {
            response.Data.Add("login", true);
        }

        return response;
    }

    // POST api/logout
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<ApiResponse> postLogout()
    {
        await _signInManager.SignOutAsync();
        ApiResponse response = new ApiResponse();
        response.Code = "200";
        response.Data.Add("message", "Logged out");
        return response;
    }

    // GET api/register
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ApiResponse> getRegisterAsync(RegisterCredential cred)
    {
        var user = new ApplicationUser
        {
            Email = cred.Email,
            UserName = cred.Email,
            SecurityStamp = Guid.NewGuid().ToString()
        };

        var result = await _userManager.CreateAsync(user, cred.Password);

        ApiResponse response = new ApiResponse();

        if (result.Succeeded)
        {
            response.Code = "200";
            response.Data.Add("message", "Registration completed");
            return response;
        }
        else
        {
            response.Code = "400";
            response.Data.Add("message", result.Errors.First().Description);
            return response;
        }
    }

}
