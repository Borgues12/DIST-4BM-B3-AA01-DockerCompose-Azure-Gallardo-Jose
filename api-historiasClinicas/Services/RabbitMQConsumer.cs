using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Microsoft.EntityFrameworkCore;
using api_historiasClinicas.Models;
using api_historiasClinicas.Data;
using api_historiasClinicas.Events;

namespace api_historiasClinicas.Services
{
    public class RabbitMQConsumer : BackgroundService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<RabbitMQConsumer> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        private IConnection? _connection;
        private IChannel? _channel;

        public RabbitMQConsumer(
            IConfiguration configuration,
            ILogger<RabbitMQConsumer> logger,
            IServiceScopeFactory scopeFactory)
        {
            _configuration = configuration;
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Reintenta conectar indefinidamente con backoff, en vez de morir al primer fallo.
            var delay = TimeSpan.FromSeconds(2);
            var maxDelay = TimeSpan.FromSeconds(30);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ConectarYConsumirAsync(stoppingToken);

                    // Si ConectarYConsumirAsync retorna normalmente es porque
                    // stoppingToken se canceló (shutdown esperado).
                    break;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Error conectando/consumiendo de RabbitMQ. Reintentando en {Delay}s...",
                        delay.TotalSeconds);

                    await LimpiarConexionAsync();

                    try
                    {
                        await Task.Delay(delay, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    // Backoff exponencial con techo de 30s
                    delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, maxDelay.TotalSeconds));
                }
            }
        }

        private async Task ConectarYConsumirAsync(CancellationToken stoppingToken)
        {
            var factory = new ConnectionFactory
            {
                HostName = _configuration["RabbitMQ:HostName"],
                Port = int.Parse(_configuration["RabbitMQ:Port"]!),
                UserName = _configuration["RabbitMQ:UserName"],
                Password = _configuration["RabbitMQ:Password"],
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
            };

            _connection = await factory.CreateConnectionAsync(stoppingToken);
            _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

            _logger.LogInformation("Conectado a RabbitMQ correctamente.");

            var queueName = _configuration["RabbitMQ:QueueName"]!;
            var queueNameUpdate = _configuration["RabbitMQ:UpdateQueueName"]!;

            await _channel.QueueDeclareAsync(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: stoppingToken
            );
            await _channel.QueueDeclareAsync(
                queue: queueNameUpdate, durable: true, exclusive: false, autoDelete: false,
                arguments: null, cancellationToken: stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(_channel);

            consumer.ReceivedAsync += async (sender, ea) =>
            {
                var body = ea.Body.ToArray();
                var mensaje = Encoding.UTF8.GetString(body);

                var evento = JsonSerializer.Deserialize<PacienteCreadoEvento>(mensaje);

                if (evento != null)
                {
                    _logger.LogInformation(
                        "Paciente creado recibido. IdPaciente: {IdPaciente}",
                        evento.IdPaciente
                    );

                    using var scope = _scopeFactory.CreateScope();

                    var dbContext = scope.ServiceProvider
                        .GetRequiredService<hcDbContext>();

                    var existe = await dbContext.Historiales
                        .AnyAsync(h => h.IdPaciente == evento.IdPaciente);

                    if (!existe)
                    {
                        var historialInicial = new HistorialClinico
                        {
                            IdPaciente = evento.IdPaciente,
                            NumHistoria = $"HC-{DateTime.Now.Year}-{evento.IdPaciente:D4}",
                            Diagnostico = "Apertura de historia médica",
                            Tratamiento = "Sin tratamiento inicial registrado",
                            Fecha = DateTime.Now
                        };

                        dbContext.Historiales.Add(historialInicial);
                        await dbContext.SaveChangesAsync();

                        _logger.LogInformation(
                            "Historial Clínico inicializado automáticamente para IdPaciente: {IdPaciente}",
                            evento.IdPaciente
                        );
                    }
                }

                await _channel!.BasicAckAsync(
                    deliveryTag: ea.DeliveryTag,
                    multiple: false
                );
            };

            var consumerUpdate = new AsyncEventingBasicConsumer(_channel);
            consumerUpdate.ReceivedAsync += async (sender, ea) =>
            {
                var body = ea.Body.ToArray();
                var mensaje = Encoding.UTF8.GetString(body);
                var evento = JsonSerializer.Deserialize<PacienteCreadoEvento>(mensaje);

                if (evento != null)
                {
                    _logger.LogInformation(
                        " > Evento PacienteActualizado recibido. IdPaciente: {IdPaciente} - No se requiere acción sobre HistorialClinico",
                        evento.IdPaciente
                    );
                }

                await _channel!.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false);
            };

            await _channel.BasicConsumeAsync(
                queue: queueName,
                autoAck: false,
                consumer: consumer,
                cancellationToken: stoppingToken
            );

            await _channel.BasicConsumeAsync(
                queue: queueNameUpdate, autoAck: false, consumer: consumerUpdate,
                cancellationToken: stoppingToken);

            // Se queda vivo hasta que cancelen el servicio o se rompa la conexión
            // (AutomaticRecoveryEnabled intentará reconectar transparentemente;
            // si falla del todo, la excepción sube y el loop externo reintenta desde cero).
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

        private Task LimpiarConexionAsync()
        {
            try
            {
                _channel?.Dispose();
                _connection?.Dispose();
            }
            catch
            {
                // Ignorar errores al limpiar conexión rota
            }
            finally
            {
                _channel = null;
                _connection = null;
            }
            return Task.CompletedTask;
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await LimpiarConexionAsync();
            await base.StopAsync(cancellationToken);
        }
    }
}