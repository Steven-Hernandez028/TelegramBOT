while (true)
{
    Console.WriteLine("queso");
    // Espera 5 minutos (300,000 milisegundos)
    await Task.Delay(TimeSpan.FromMinutes(5));
}