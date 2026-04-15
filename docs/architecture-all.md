# FlowPilot AI — Architecture Documentation

## 1. System Architecture — Layers & Components

```mermaid
graph TB
    subgraph API["FlowPilot.Api — Entry Point"]
        direction TB
        MW["Middleware<br/>PublicTenantMiddleware"]
        ENDPOINTS["Minimal API Endpoints<br/>Appointments | Customers | Services<br/>Templates | Settings | Stats<br/>Auth | Messaging | Public Booking<br/>Twilio Webhooks"]
        HUBS["SignalR Hubs<br/>AppointmentHub | SmsHub"]
        FILTERS["Filters<br/>TwilioSignatureFilter"]
        HTTP_TENANT["HttpCurrentTenant<br/>JWT claims + PublicTenantId fallback"]
    end

    subgraph APP["FlowPilot.Application — Contracts & Events"]
        direction TB
        IFACES["Service Interfaces<br/>IAppointmentService | ICustomerService<br/>IMessagingService | ITemplateService<br/>IAuthService | IServiceService<br/>ITenantSettingsService | IDashboardStatsService<br/>IPublicBookingService"]
        EVENTS["MediatR Events<br/>AppointmentCreatedEvent<br/>AppointmentStatusChangedEvent<br/>CustomerOptedOutEvent<br/>InboundSmsReceivedEvent"]
        DTOS["DTOs (records)<br/>AppointmentDto | CustomerDto<br/>MessageDto | ConversationSummaryDto<br/>PublicBookingRequest | TimeSlotDto"]
        AGENT_IFACES["Agent Interfaces<br/>IAgentOrchestrator | IAgentTool<br/>IToolRegistry"]
    end

    subgraph INFRA["FlowPilot.Infrastructure — Implementations"]
        direction TB
        subgraph MODULES["Bounded Context Modules"]
            direction LR
            M_APT["Appointments"]
            M_CUST["Customers"]
            M_MSG["Messaging"]
            M_TMPL["Templates"]
            M_SVC["Services"]
            M_SET["Settings"]
            M_AUTH["Auth"]
            M_BOOK["PublicBooking"]
            M_STATS["Stats"]
        end
        subgraph AGENTS["AI Agent Layer"]
            direction LR
            ORCH["AgentOrchestrator<br/>Azure OpenAI"]
            REM_AGENT["ReminderOptimization<br/>Agent"]
            REPLY_AGENT["ReplyHandling<br/>Agent"]
            REVIEW_AGENT["ReviewRecovery<br/>Agent"]
        end
        subgraph TOOLS["Agent Tools (8)"]
            direction LR
            T1["GetCustomerHistory"]
            T2["GetAppointmentDetails"]
            T3["ScheduleSms"]
            T4["SendSms"]
            T5["ClassifyIntent"]
            T6["ConfirmAppointment"]
            T7["CheckReviewCooldown"]
            T8["GetReviewPlatforms"]
        end
        subgraph PERSIST["Persistence"]
            direction LR
            DBCTX["AppDbContext<br/>Global filters: TenantId + IsDeleted"]
            SMS_PROV["ISmsProvider<br/>TwilioSmsProvider | FakeSmsProvider"]
            TMPL_REND["TemplateRenderer"]
        end
    end

    subgraph DOMAIN["FlowPilot.Domain — Entities & Rules"]
        direction TB
        ENTITIES["17 Entities (all inherit BaseEntity)<br/>Tenant | User | Customer | ConsentRecord<br/>Appointment | Service | AuditLog<br/>Message | ScheduledMessage<br/>Template | TemplateLocaleVariant<br/>Plan | UsageRecord | TenantSettings<br/>AgentRun | ToolCallLog | ProcessedEvent"]
        ENUMS["7 Enums<br/>AppointmentStatus | ConsentStatus | ConsentSource<br/>MessageDirection | MessageStatus<br/>ScheduledMessageStatus | UserRole"]
        BASE["BaseEntity<br/>Id | TenantId | CreatedAt | UpdatedAt<br/>IsDeleted | DeletedAt"]
    end

    subgraph SHARED["FlowPilot.Shared — Cross-Cutting"]
        direction LR
        RESULT["Result&lt;T&gt; Pattern"]
        ITENANT["ICurrentTenant"]
        IGATE["IFeatureGate"]
    end

    subgraph WORKERS["FlowPilot.Workers — Background Services"]
        direction LR
        W1["ScheduledMessage<br/>Dispatcher<br/>polls 30s"]
        W2["Appointment<br/>AutoCompletion<br/>Confirmed->Completed"]
        W3["Appointment<br/>AutoConfirm<br/>Scheduled->Confirmed<br/>3h before"]
    end

    subgraph WEB["FlowPilot.Web — React Frontend"]
        direction LR
        PAGES["Pages: Dashboard | Customers<br/>Appointments | SMS Inbox<br/>Templates | Settings | Booking"]
        TECH["TanStack Query | React Hook Form<br/>Zod | Tailwind | SignalR hooks"]
    end

    API --> APP
    API --> INFRA
    INFRA --> APP
    INFRA --> DOMAIN
    APP --> DOMAIN
    APP --> SHARED
    DOMAIN --> SHARED
    WORKERS --> INFRA
    WEB -.->|HTTP + SignalR| API

    classDef apiStyle fill:#4A90D9,stroke:#2C5F8A,color:#fff
    classDef appStyle fill:#7B68EE,stroke:#5A4CBE,color:#fff
    classDef infraStyle fill:#E8913A,stroke:#C47830,color:#fff
    classDef domainStyle fill:#50C878,stroke:#3DA660,color:#fff
    classDef sharedStyle fill:#808080,stroke:#606060,color:#fff
    classDef workerStyle fill:#DC143C,stroke:#B01030,color:#fff
    classDef webStyle fill:#20B2AA,stroke:#1A8F88,color:#fff

    class API apiStyle
    class APP appStyle
    class INFRA infraStyle
    class DOMAIN domainStyle
    class SHARED sharedStyle
    class WORKERS workerStyle
    class WEB webStyle
```

## 2. Event-Driven Architecture — MediatR Event Flow

```mermaid
graph LR
    subgraph TRIGGERS["Triggers"]
        BOOK["Customer Books<br/>(Public Page)"]
        WORKER_CONFIRM["AutoConfirm Worker<br/>(3h before)"]
        WORKER_COMPLETE["AutoCompletion Worker<br/>(after EndsAt)"]
        TWILIO_IN["Twilio Webhook<br/>(Inbound SMS)"]
        STAFF["Staff Action<br/>(Dashboard)"]
    end

    subgraph EVENTS["MediatR Domain Events"]
        ACE["AppointmentCreated<br/>Event"]
        ASCE["AppointmentStatus<br/>ChangedEvent"]
        COE["CustomerOptedOut<br/>Event"]
        ISRE["InboundSmsReceived<br/>Event"]
    end

    subgraph HANDLERS["Event Handlers"]
        H1["AppointmentBooked<br/>SmsHandler<br/>instant confirmation SMS"]
        H2["ReminderOptimization<br/>Agent<br/>schedules 2 reminders"]
        H3["AppointmentRealtime<br/>Handler<br/>pushes to SignalR"]
        H4["AppointmentStatus<br/>ChangedHandler<br/>creates audit log"]
        H5["ReviewRecovery<br/>Agent<br/>sends review request"]
        H6["CustomerOptedOut<br/>Handler<br/>cancels pending SMS"]
        H7["SmsRealtime<br/>Handler<br/>pushes to SignalR +<br/>ReplyHandlingAgent"]
    end

    subgraph EFFECTS["Side Effects"]
        SMS_OUT["SMS Sent<br/>(Twilio)"]
        SIGNALR["SignalR Push<br/>(Dashboard)"]
        DB["DB Update<br/>(Status / Audit)"]
        SCHEDULED["ScheduledMessage<br/>Created"]
    end

    BOOK --> ACE
    WORKER_CONFIRM --> ASCE
    WORKER_COMPLETE --> ASCE
    TWILIO_IN --> ISRE
    TWILIO_IN -->|STOP keyword| COE
    STAFF --> ASCE

    ACE --> H1
    ACE --> H2
    ACE --> H3
    ASCE --> H4
    ASCE --> H5
    COE --> H6
    ISRE --> H7

    H1 --> SMS_OUT
    H2 --> SCHEDULED
    H3 --> SIGNALR
    H4 --> DB
    H5 --> SMS_OUT
    H6 --> DB
    H7 --> SIGNALR
    H7 -->|agent classifies| DB

    classDef triggerStyle fill:#4A90D9,stroke:#2C5F8A,color:#fff
    classDef eventStyle fill:#E8913A,stroke:#C47830,color:#fff
    classDef handlerStyle fill:#7B68EE,stroke:#5A4CBE,color:#fff
    classDef effectStyle fill:#50C878,stroke:#3DA660,color:#fff

    class TRIGGERS triggerStyle
    class EVENTS eventStyle
    class HANDLERS handlerStyle
    class EFFECTS effectStyle
```

## 3. End-to-End SMS Lifecycle — Sequence Diagram

```mermaid
sequenceDiagram
    participant C as Customer Phone
    participant T as Twilio
    participant API as FlowPilot API
    participant MS as MessagingService
    participant MR as MediatR
    participant RA as ReplyHandling Agent
    participant AI as Azure OpenAI
    participant DB as PostgreSQL
    participant SR as SignalR
    participant D as Staff Dashboard

    Note over C,D: BOOKING FLOW
    C->>API: POST /api/v1/public/book/{slug}
    API->>DB: Find-or-create Customer (OptedIn)
    API->>DB: Create Appointment (Scheduled)
    API->>MR: Publish AppointmentCreatedEvent

    par Instant Confirmation
        MR->>MS: AppointmentBookedSmsHandler
        MS->>T: Send SMS (FR/EN)
        T->>C: "Votre RDV est planifie. Repondez OUI!"
    and Schedule Reminders
        MR->>AI: ReminderOptimizationAgent
        AI->>DB: ScheduleSms (24h before)
        AI->>DB: ScheduleSms (3-4h before)
    and Real-time Update
        MR->>SR: AppointmentRealtimeHandler
        SR->>D: New appointment notification
    end

    Note over C,D: REMINDER DISPATCH (Worker)
    DB-->>MS: ScheduledMessageDispatcher polls
    MS->>T: Send reminder SMS
    T->>C: "Rappel: votre RDV demain a 10h. OUI pour confirmer"

    Note over C,D: CUSTOMER REPLY
    C->>T: "Oui je confirme"
    T->>API: POST /api/webhooks/twilio/incoming
    API->>API: TwilioSignatureFilter (HMAC-SHA1)
    API->>MS: ProcessInboundAsync
    MS->>DB: Save inbound Message
    MS->>MR: Publish InboundSmsReceivedEvent

    par Real-time + Agent
        MR->>SR: SmsRealtimeHandler
        SR->>D: New message in inbox
    and Intent Classification
        MR->>RA: ReplyHandlingAgent
        RA->>AI: Classify intent (all upcoming appointments)
        AI-->>RA: Confirm, confidence 0.92
        RA->>DB: Appointment.Status = Confirmed
        RA->>MR: Publish AppointmentStatusChangedEvent
    end

    Note over C,D: AUTO-CONFIRM (if no reply)
    Note right of DB: AppointmentAutoConfirmWorker<br/>Scheduled -> Confirmed<br/>(3h before StartsAt)

    Note over C,D: AUTO-COMPLETE (after appointment)
    Note right of DB: AppointmentAutoCompletionWorker<br/>Confirmed -> Completed<br/>(after EndsAt)
```

## 4. Appointment State Machine

```mermaid
stateDiagram-v2
    [*] --> Scheduled: Customer books<br/>(public page or staff)

    Scheduled --> Confirmed: Customer replies YES<br/>(ReplyHandlingAgent)
    Scheduled --> Confirmed: Auto-confirm worker<br/>(3h before StartsAt)
    Scheduled --> Cancelled: Customer replies CANCEL<br/>(ReplyHandlingAgent)
    Scheduled --> Cancelled: Staff cancels
    Scheduled --> Rescheduled: Staff reschedules<br/>(creates new appointment)

    Confirmed --> Completed: Auto-completion worker<br/>(after EndsAt)
    Confirmed --> Cancelled: Staff cancels
    Confirmed --> Rescheduled: Staff reschedules<br/>(creates new appointment)

    Completed --> [*]
    Cancelled --> [*]
    Rescheduled --> [*]

    note right of Scheduled
        Triggers on entry:
        - AppointmentBookedSmsHandler (instant SMS)
        - ReminderOptimizationAgent (schedule reminders)
        - AppointmentRealtimeHandler (SignalR)
    end note

    note right of Confirmed
        Triggers on transition:
        - AppointmentStatusChangedHandler (audit log)
    end note

    note right of Completed
        Triggers on transition:
        - AppointmentStatusChangedHandler (audit log)
        - ReviewRecoveryAgent (review request SMS)
    end note
```
