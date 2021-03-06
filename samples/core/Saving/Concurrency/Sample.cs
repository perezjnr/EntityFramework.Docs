﻿using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace EFSaving.Concurrency
{
    public class Sample
    {
        public static async Task RunAsync()
        {
            // Ensure database is created and has a person in it
            await using (var setupContext = new PersonContext())
            {
                await setupContext.Database.EnsureDeletedAsync();
                await setupContext.Database.EnsureCreatedAsync();

                setupContext.People.Add(new Person { FirstName = "John", LastName = "Doe" });
                await setupContext.SaveChangesAsync();
            }

            #region ConcurrencyHandlingCode
            await using var context = new PersonContext();
            // Fetch a person from database and change phone number
            var person = context.People.Single(p => p.PersonId == 1);
            person.PhoneNumber = "555-555-5555";

            // Change the person's name in the database to simulate a concurrency conflict
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.People SET FirstName = 'Jane' WHERE PersonId = 1");

            var saved = false;
            while (!saved)
            {
                try
                {
                    // Attempt to save changes to the database
                    await context.SaveChangesAsync();
                    saved = true;
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    foreach (var entry in ex.Entries)
                    {
                        if (entry.Entity is Person)
                        {
                            var proposedValues = entry.CurrentValues;
                            var databaseValues = await entry.GetDatabaseValuesAsync();

                            foreach (var property in proposedValues.Properties)
                            {
                                var proposedValue = proposedValues[property];
                                var databaseValue = databaseValues[property];

                                // TODO: decide which value should be written to database
                                // proposedValues[property] = <value to be saved>;
                            }

                            // Refresh original values to bypass next concurrency check
                            entry.OriginalValues.SetValues(databaseValues);
                        }
                        else
                        {
                            throw new NotSupportedException(
                                "Don't know how to handle concurrency conflicts for "
                                + entry.Metadata.Name);
                        }
                    }
                }
            }
            #endregion
        }

        public class PersonContext : DbContext
        {
            public DbSet<Person> People { get; set; }

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
                // Requires NuGet package Microsoft.EntityFrameworkCore.SqlServer
                optionsBuilder.UseSqlServer(
                    @"Server=(localdb)\mssqllocaldb;Database=EFSaving.Concurrency;Trusted_Connection=True;ConnectRetryCount=0");
            }
        }

        public class Person
        {
            public int PersonId { get; set; }

            [ConcurrencyCheck]
            public string FirstName { get; set; }

            [ConcurrencyCheck]
            public string LastName { get; set; }

            public string PhoneNumber { get; set; }
        }
    }
}
