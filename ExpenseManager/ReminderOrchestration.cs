using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Assistant
{
    public static class ReminderOrchestration
    {
        private const string ReminderCreatedEvent = "ReminderCreatedEvent";

        private class State{
            public State()
            {
                Reminders = new List<Reminder>();
            }

            public List<Reminder> Reminders { get; set; }
            public DateTime LastTriggerTime { get; set; }
        }

        [FunctionName("ReminderOrchestration")]
        public static async Task RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var state = context.GetInput<State>() ?? new State{
                LastTriggerTime = context.CurrentUtcDateTime
            };

            var cancellationTokenSource = new CancellationTokenSource();
            var nextReminderMailTime = state.LastTriggerTime.AddTime(1);
            var timerTask = context.CreateTimer(nextReminderMailTime, cancellationTokenSource.Token);
            var newReminderTask = context.WaitForExternalEvent<Reminder>(ReminderCreatedEvent);

           var completedTask = await Task.WhenAny(timerTask, newReminderTask);
            if (completedTask == timerTask)
            {
                var remindersToSend = state.Reminders.Where(r => r.NextRun <= context.CurrentUtcDateTime).ToArray();
                await context.CallActivityAsync<string>("ReminderOrchestration_SendReminder", remindersToSend);
                foreach(var reminder in remindersToSend){
                    if(!reminder.RepeatCount.HasValue){
                        reminder.RepeatCount--;
                    }
                    
                    if(reminder.RepeatCount.HasValue && reminder.RepeatCount > 0){
                        reminder.NextRun = context.CurrentUtcDateTime.AddTime(reminder.RepeatAfterNumberOfDays);
                    }else{
                        state.Reminders.Remove(reminder);
                    }
                }
                state.LastTriggerTime = context.CurrentUtcDateTime;
            }
            else
            {
                var reminder = newReminderTask.Result;
                reminder.NextRun = context.CurrentUtcDateTime.AddTime(reminder.RepeatAfterNumberOfDays);
                state.Reminders.Add(reminder);
            }

            context.ContinueAsNew(state);
        }

        private static DateTime AddTime(this DateTime date, int days){
            #if DEBUG
            return date.AddSeconds(days * 15);
            #else
            return date.AddDays(days);
            #endif
        }

        [FunctionName("ReminderOrchestration_SendReminder")]
        public static void SendReminder([ActivityTrigger] Reminder[] reminders, ILogger log)
        {
            foreach(var reminder in reminders){
                log.LogInformation($"Reminder to {reminder.Title}");
            }
        }

        [FunctionName("ReminderOrchestration_CreateReminder")]
        public static async Task<IActionResult> CreateReminder([HttpTrigger(AuthorizationLevel.Function, "post", Route = "{instanceId}/reminders")] HttpRequest req,
            string instanceId,
            [DurableClient] IDurableOrchestrationClient orchestrationClient,
            ILogger log)
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var reminder = JsonConvert.DeserializeObject<Reminder>(requestBody);

            if(string.IsNullOrEmpty(reminder.Title)){
                return new BadRequestResult();
            }
            if(reminder.RepeatAfterNumberOfDays <= 0){
                return new BadRequestResult();
            }
            if(reminder.RepeatCount.HasValue && reminder.RepeatCount <= 0){
                return new BadRequestResult();
            }

            await orchestrationClient.RaiseEventAsync(instanceId, ReminderCreatedEvent, reminder);

            return new OkObjectResult(reminder);
        }


        [FunctionName("ReminderOrchestration_HttpStart")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            // Function input comes from the request content.
            string instanceId = await starter.StartNewAsync("ReminderOrchestration", null);

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }

        public class Reminder{
            public DateTime NextRun {get;set;}
            public int RepeatAfterNumberOfDays { get; set; }    
            public int? RepeatCount {get;set;}
            public string Title { get; set; }
        }
    }
}