using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Expenses
{
    public static class ExpenseTrigger
    {
        [FunctionName("ExpenseTrigger")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var expense = JsonConvert.DeserializeObject<Expense>(requestBody);

            if(string.IsNullOrEmpty(expense.Name)){
                return new BadRequestResult();
            }
            if(expense.Amount <= 0){
                return new BadRequestResult();
            }

            return new OkObjectResult(expense.Name);
        }
    }

    public class Expense{
        public string Name { get; set; }
        public decimal Amount { get; set; }
        public DateTime Date {get;set;}        
    }
}
