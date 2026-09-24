# Bismarck PAM Monitoring Guide

## Overview

Bismarck PAM uses Prometheus and Grafana to monitor the .NET microservices and Kafka/Redpanda messaging infrastructure.

The monitoring system provides visibility into:

- Service availability
- HTTP request count
- HTTP request rate
- HTTP 4xx errors
- HTTP 5xx errors
- CPU usage
- Memory usage
- Kafka broker availability
- Kafka consumer lag

---

## Monitoring Architecture

```text
.NET Microservices
      |
      | /metrics
      v
  Prometheus
      |
      v
   Grafana
```

Kafka monitoring works as:

```text
Kafka / Redpanda
      |
      v
 Kafka Exporter
      |
      v
  Prometheus
      |
      v
   Grafana
```

---

## Components

### Prometheus

Prometheus collects metrics from all application services and Kafka Exporter.

Local URL:

```text
http://localhost:9090
```

Configuration file:

```text
monitoring/prometheus/prometheus.yml
```

---

### Grafana

Grafana displays the collected metrics.

Local URL:

```text
http://localhost:3000
```

Grafana credentials are configured using environment variables:

```text
GRAFANA_ADMIN_USER
GRAFANA_ADMIN_PASSWORD
```

Real passwords must not be committed to source control.

---

### Kafka Exporter

Kafka Exporter exposes Kafka broker and consumer-group information as Prometheus metrics.

Local metrics endpoint:

```text
http://localhost:9308/metrics
```

---

## Monitored Services

The following Bismarck services expose Prometheus metrics through `/metrics`:

- Identity Service
- Resource Service
- Authorization Service
- Approval Service
- Audit Service
- Notification Service
- Gateway Service

Prometheus uses the Docker Compose service names:

```text
identity_svc:8080
resource_svc:8080
authorization_svc:8080
approval_svc:8080
audit_svc:8080
notification_svc:8080
gateway_svc:8080
```

---

## Start Monitoring Infrastructure

Docker Desktop must be running.

From the repository root:

```powershell
docker compose up -d kafka kafka-exporter prometheus grafana
```

Check running containers:

```powershell
docker compose ps
```

Check Prometheus:

```powershell
Invoke-WebRequest http://localhost:9090/-/ready
```

Check Kafka Exporter:

```powershell
Invoke-WebRequest http://localhost:9308/metrics
```

Check Grafana:

```powershell
Invoke-WebRequest http://localhost:3000/api/health
```

---

## Grafana Dashboard

Dashboard name:

```text
Bismarck PAM — Service & Kafka Monitoring
```

Dashboard definition:

```text
monitoring/grafana/dashboards/bismarck-pam-monitoring-dashboard.json
```

The dashboard contains:

1. Service Health
2. Total HTTP Requests
3. Kafka Broker Count
4. HTTP Request Rate
5. HTTP 4xx Error Rate
6. HTTP 5xx Error Rate
7. CPU Usage per Service
8. Memory Usage per Service
9. Kafka Consumer Lag

---

## Service Health

Prometheus query:

```promql
up{service!=""}
```

Meaning:

```text
1 = UP
0 = DOWN
```

A DOWN service means Prometheus cannot currently scrape its `/metrics` endpoint.

---

## Total HTTP Requests

```promql
sum by (service) (
  http_requests_received_total{service!=""}
)
```

---

## HTTP Request Rate

```promql
sum by (service) (
  rate(http_requests_received_total{service!=""}[$__rate_interval])
)
```

---

## HTTP 4xx Error Rate

```promql
sum by (service) (
  rate(http_requests_received_total{service!="",code=~"4.."}[$__rate_interval])
)
```

---

## HTTP 5xx Error Rate

```promql
sum by (service) (
  rate(http_requests_received_total{service!="",code=~"5.."}[$__rate_interval])
)
```

If no HTTP 5xx responses occur during the selected time range, Grafana may display no data.

---

## CPU Usage

```promql
system_runtime_cpu_usage{service!=""}
```

---

## Memory Usage

```promql
system_runtime_working_set{service!=""}
```

---

## Kafka Broker Monitoring

Kafka Exporter provides the following broker metric:

```promql
kafka_brokers
```

For the local single-node Redpanda environment, the expected value is:

```text
1
```

---

## Kafka Consumer Lag

Consumer lag indicates how many Kafka messages are waiting to be processed by a consumer group.

Grafana query:

```promql
sum by (consumergroup, topic) (
  kafka_consumergroup_lag
)
```

During local verification, a test consumer group demonstrated:

```text
Consumer Group: bismarck-monitoring-test
Topic: identity-events

Current Offset: 1
Log End Offset: 9
Lag: 8
```

After the remaining messages were consumed:

```text
Current Offset: 9
Log End Offset: 9
Lag: 0
```

Grafana successfully visualized the consumer lag changing from:

```text
8 -> 0
```

---

## Kafka Verification Commands

List topics:

```powershell
docker exec kafka rpk topic list --brokers localhost:9092
```

List consumer groups:

```powershell
docker exec kafka rpk group list --brokers localhost:9092
```

Inspect a consumer group:

```powershell
docker exec kafka rpk group describe bismarck-monitoring-test --brokers localhost:9092
```

Check consumer-group metrics from Kafka Exporter:

```powershell
(Invoke-WebRequest http://localhost:9308/metrics -UseBasicParsing).Content |
Select-String "kafka_consumergroup"
```

Check consumer lag through Prometheus:

```powershell
Invoke-RestMethod "http://localhost:9090/api/v1/query?query=sum%20by%20(consumergroup%2Ctopic)%20(kafka_consumergroup_lag)" |
ConvertTo-Json -Depth 10
```

---

## HTTP Monitoring Test

HTTP traffic can be generated against a locally running service to verify request-rate and error-rate graphs.

Example:

```powershell
1..40 | ForEach-Object {
    try {
        Invoke-WebRequest "http://localhost:5080/monitoring-test" `
            -UseBasicParsing `
            -ErrorAction Stop | Out-Null
    }
    catch {
        # Expected 404 response for monitoring test
    }

    Start-Sleep -Milliseconds 500
}
```

This test demonstrated:

```text
Total HTTP Requests: 60
HTTP Request Rate: approximately 0.889 requests/second
HTTP 4xx Error Rate: approximately 0.889 requests/second
```

---

## Troubleshooting

### Service shows DOWN

Check whether the service is running:

```powershell
docker compose ps
```

A service that is intentionally stopped will correctly appear as DOWN in Grafana.

### HTTP Request Rate shows zero

The request rate depends on recent traffic.

Generate requests and refresh the Grafana dashboard.

### HTTP 5xx panel shows No Data

This is expected when no HTTP 5xx responses have occurred in the selected period.

### Kafka Consumer Lag shows No Data

Check whether Kafka contains a consumer group with committed offsets:

```powershell
docker exec kafka rpk group list --brokers localhost:9092
```

### Kafka Broker Count shows No Data

Check Kafka and Kafka Exporter:

```powershell
docker compose ps kafka kafka-exporter
```

---

## Security Notes

- Do not commit `.env` files containing credentials.
- Do not hard-code Grafana administrator passwords.
- Keep production secrets outside source control.
- Restrict Prometheus and Grafana access appropriately in production.
- Restrict `/metrics` endpoints appropriately in production environments.

---

## BIS-308 Verification Summary

The BIS-308 monitoring implementation provides:

- Prometheus metrics for the .NET services
- Prometheus scrape configuration for all application services
- Kafka Exporter integration
- Grafana datasource provisioning
- Grafana dashboard provisioning
- Service health monitoring
- HTTP request monitoring
- HTTP 4xx and 5xx monitoring
- CPU monitoring
- Memory monitoring
- Kafka broker monitoring
- Kafka consumer lag monitoring
- Docker Compose integration
- Monitoring documentation