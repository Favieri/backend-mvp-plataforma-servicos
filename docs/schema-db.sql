-- WARNING: This schema is for context only and is not meant to be run.
-- Table order and constraints may not be valid for execution.

CREATE TABLE public.Appointment (
  id text NOT NULL,
  professionalId text NOT NULL,
  clientId text,
  serviceId text,
  startsAt timestamp without time zone NOT NULL,
  endsAt timestamp without time zone NOT NULL,
  status USER-DEFINED NOT NULL DEFAULT 'PENDING'::"AppointmentStatus",
  location text,
  notes text,
  createdAt timestamp without time zone NOT NULL DEFAULT now(),
  updatedAt timestamp without time zone NOT NULL DEFAULT now(),
  CONSTRAINT Appointment_pkey PRIMARY KEY (id),
  CONSTRAINT Appointment_professionalId_fkey FOREIGN KEY (professionalId) REFERENCES public.Professional(id)
);
ProfessionalReadRepository public.Conversation (
  id text NOT NULL,
  orderId text,
  clientId text NOT NULL,
  professionalId text NOT NULL,
  createdAt timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
  clientLastReadAt timestamp with time zone,
  professionalLastReadAt timestamp with time zone,
  CONSTRAINT Conversation_pkey PRIMARY KEY (id),
  CONSTRAINT Conversation_orderId_fkey FOREIGN KEY (orderId) REFERENCES public.Order(id),
  CONSTRAINT Conversation_clientId_fkey FOREIGN KEY (clientId) REFERENCES public.User(id),
  CONSTRAINT Conversation_professionalId_fkey FOREIGN KEY (professionalId) REFERENCES public.User(id)
);
CREATE TABLE public.EmailJob (
  id text NOT NULL,
  to text NOT NULL,
  subject text NOT NULL,
  html text NOT NULL,
  text text,
  replyTo text,
  from text,
  attempts integer NOT NULL DEFAULT 0,
  status text NOT NULL DEFAULT 'pending'::text CHECK (status = ANY (ARRAY['pending'::text, 'processing'::text, 'sent'::text, 'failed'::text])),
  error text,
  dedupeKey text,
  createdAt timestamp with time zone NOT NULL DEFAULT now(),
  sentAt timestamp with time zone,
  CONSTRAINT EmailJob_pkey PRIMARY KEY (id)
);
CREATE TABLE public.Message (
  id text NOT NULL,
  conversationId text NOT NULL,
  senderId text NOT NULL,
  text text NOT NULL,
  sentAt timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT Message_pkey PRIMARY KEY (id),
  CONSTRAINT Message_conversationId_fkey FOREIGN KEY (conversationId) REFERENCES public.Conversation(id),
  CONSTRAINT Message_senderId_fkey FOREIGN KEY (senderId) REFERENCES public.User(id)
);
CREATE TABLE public.Order (
  id text NOT NULL,
  clientId text NOT NULL,
  serviceId text NOT NULL,
  description text,
  location text,
  date timestamp without time zone,
  status text NOT NULL DEFAULT 'aberto'::text,
  createdAt timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT Order_pkey PRIMARY KEY (id),
  CONSTRAINT Order_clientId_fkey FOREIGN KEY (clientId) REFERENCES public.User(id),
  CONSTRAINT Order_serviceId_fkey FOREIGN KEY (serviceId) REFERENCES public.Service(id)
);
CREATE TABLE public.Professional (
  id text NOT NULL,
  userId text NOT NULL,
  bio text,
  rating double precision DEFAULT 0,
  active boolean NOT NULL DEFAULT true,
  avatarUrl text,
  availabilityText text,
  completedJobsCount integer NOT NULL DEFAULT 0,
  slotMinutes integer DEFAULT 60,
  leadTimeMinutes integer DEFAULT 120,
  maxAdvanceDays integer DEFAULT 30,
  allowInstantBooking boolean DEFAULT false,
  CONSTRAINT Professional_pkey PRIMARY KEY (id),
  CONSTRAINT Professional_userId_fkey FOREIGN KEY (userId) REFERENCES public.User(id)
);
CREATE TABLE public.ProfessionalAvailability (
  id text NOT NULL,
  professionalId text NOT NULL,
  weekday integer NOT NULL,
  startMinutes integer NOT NULL,
  endMinutes integer NOT NULL,
  active boolean NOT NULL DEFAULT true,
  CONSTRAINT ProfessionalAvailability_pkey PRIMARY KEY (id),
  CONSTRAINT ProfessionalAvailability_professionalId_fkey FOREIGN KEY (professionalId) REFERENCES public.Professional(id)
);
CREATE TABLE public.ProfessionalBlock (
  id text NOT NULL DEFAULT (gen_random_uuid())::text,
  professionalId text NOT NULL,
  startsAt timestamp with time zone NOT NULL,
  endsAt timestamp with time zone NOT NULL,
  reason text,
  createdAt timestamp with time zone NOT NULL DEFAULT now(),
  CONSTRAINT ProfessionalBlock_pkey PRIMARY KEY (id),
  CONSTRAINT ProfessionalBlock_professionalId_fkey FOREIGN KEY (professionalId) REFERENCES public.Professional(id)
);
CREATE TABLE public.ProfessionalOrderIgnore (
  professionalId text NOT NULL,
  orderId text NOT NULL,
  createdAt timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT ProfessionalOrderIgnore_pkey PRIMARY KEY (professionalId, orderId),
  CONSTRAINT ProfessionalOrderIgnore_professionalId_fkey FOREIGN KEY (professionalId) REFERENCES public.Professional(id),
  CONSTRAINT ProfessionalOrderIgnore_orderId_fkey FOREIGN KEY (orderId) REFERENCES public.Order(id)
);
CREATE TABLE public.ProfessionalPortfolio (
  id text NOT NULL DEFAULT gen_random_uuid(),
  professionalId text NOT NULL,
  imageUrl text NOT NULL,
  title text,
  description text,
  orderIndex integer,
  createdAt timestamp with time zone NOT NULL DEFAULT now(),
  CONSTRAINT ProfessionalPortfolio_pkey PRIMARY KEY (id),
  CONSTRAINT ProfessionalPortfolio_professionalId_fkey FOREIGN KEY (professionalId) REFERENCES public.Professional(id)
);
CREATE TABLE public.ProfessionalService (
  id text NOT NULL,
  professionalId text NOT NULL,
  serviceId text NOT NULL,
  nomeServico text NOT NULL,
  preco double precision NOT NULL,
  descricao text,
  CONSTRAINT ProfessionalService_pkey PRIMARY KEY (id),
  CONSTRAINT ProfessionalService_professionalId_fkey FOREIGN KEY (professionalId) REFERENCES public.Professional(id),
  CONSTRAINT ProfessionalService_serviceId_fkey FOREIGN KEY (serviceId) REFERENCES public.Service(id)
);
CREATE TABLE public.ProfessionalZone (
  professionalId text NOT NULL,
  zoneId text NOT NULL,
  createdAt timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT ProfessionalZone_pkey PRIMARY KEY (professionalId, zoneId),
  CONSTRAINT ProfessionalZone_professionalId_fkey FOREIGN KEY (professionalId) REFERENCES public.Professional(id),
  CONSTRAINT ProfessionalZone_zoneId_fkey FOREIGN KEY (zoneId) REFERENCES public.Zone(id)
);
CREATE TABLE public.Review (
  id text NOT NULL DEFAULT gen_random_uuid(),
  orderId text NOT NULL UNIQUE,
  professionalId text NOT NULL,
  clientId text NOT NULL,
  rating integer NOT NULL,
  comment text,
  createdAt timestamp with time zone NOT NULL DEFAULT now(),
  CONSTRAINT Review_pkey PRIMARY KEY (id),
  CONSTRAINT Review_orderId_fkey FOREIGN KEY (orderId) REFERENCES public.Order(id),
  CONSTRAINT Review_professionalId_fkey FOREIGN KEY (professionalId) REFERENCES public.Professional(id),
  CONSTRAINT Review_clientId_fkey FOREIGN KEY (clientId) REFERENCES public.User(id)
);
CREATE TABLE public.Service (
  id text NOT NULL,
  name text NOT NULL,
  icon text,
  createdAt timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT Service_pkey PRIMARY KEY (id)
);
CREATE TABLE public.User (
  id text NOT NULL,
  name text NOT NULL,
  email text NOT NULL,
  phone text,
  role text NOT NULL,
  createdAt timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
  senha text,
  zoneId text,
  CONSTRAINT User_pkey PRIMARY KEY (id),
  CONSTRAINT User_zoneId_fkey FOREIGN KEY (zoneId) REFERENCES public.Zone(id)
);
CREATE TABLE public.Zone (
  id text NOT NULL,
  name text NOT NULL UNIQUE,
  active boolean NOT NULL DEFAULT true,
  createdAt timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT Zone_pkey PRIMARY KEY (id)
);
CREATE TABLE public.ledger_entry (
  id uuid NOT NULL DEFAULT gen_random_uuid(),
  type USER-DEFINED NOT NULL,
  order_id uuid,
  payment_id uuid,
  payout_item_id uuid,
  professional_id uuid,
  amount_cents integer NOT NULL,
  created_at timestamp with time zone NOT NULL DEFAULT now(),
  CONSTRAINT ledger_entry_pkey PRIMARY KEY (id),
  CONSTRAINT ledger_entry_order_id_fkey FOREIGN KEY (order_id) REFERENCES public.order(id),
  CONSTRAINT ledger_entry_payment_id_fkey FOREIGN KEY (payment_id) REFERENCES public.payment(id),
  CONSTRAINT ledger_entry_payout_item_id_fkey FOREIGN KEY (payout_item_id) REFERENCES public.payoutitem(id),
  CONSTRAINT ledger_entry_professional_id_fkey FOREIGN KEY (professional_id) REFERENCES public.professional(id),
  CONSTRAINT fk_ledger_order FOREIGN KEY (order_id) REFERENCES public.order(id),
  CONSTRAINT fk_ledger_payment FOREIGN KEY (payment_id) REFERENCES public.payment(id),
  CONSTRAINT fk_ledger_payoutitem FOREIGN KEY (payout_item_id) REFERENCES public.payoutitem(id),
  CONSTRAINT fk_ledger_professional FOREIGN KEY (professional_id) REFERENCES public.professional(id)
);
CREATE TABLE public.order (
  id uuid NOT NULL DEFAULT gen_random_uuid(),
  customer_id text NOT NULL,
  professional_id uuid NOT NULL,
  amount_cents integer NOT NULL,
  status text NOT NULL,
  created_at timestamp with time zone NOT NULL DEFAULT now(),
  updated_at timestamp with time zone NOT NULL DEFAULT now(),
  completed_at timestamp with time zone,
  CONSTRAINT order_pkey PRIMARY KEY (id),
  CONSTRAINT order_professional_id_fkey FOREIGN KEY (professional_id) REFERENCES public.professional(id),
  CONSTRAINT fk_order_professional FOREIGN KEY (professional_id) REFERENCES public.professional(id)
);
CREATE TABLE public.payable (
  id uuid NOT NULL DEFAULT gen_random_uuid(),
  professional_id uuid NOT NULL,
  order_id uuid NOT NULL,
  amount_cents integer NOT NULL,
  status USER-DEFINED NOT NULL,
  hold_until timestamp with time zone,
  created_at timestamp with time zone NOT NULL DEFAULT now(),
  payout_item_id uuid,
  CONSTRAINT payable_pkey PRIMARY KEY (id),
  CONSTRAINT payable_professional_id_fkey FOREIGN KEY (professional_id) REFERENCES public.professional(id),
  CONSTRAINT payable_order_id_fkey FOREIGN KEY (order_id) REFERENCES public.order(id),
  CONSTRAINT fk_payable_payoutitem FOREIGN KEY (payout_item_id) REFERENCES public.payoutitem(id),
  CONSTRAINT fk_payable_professional FOREIGN KEY (professional_id) REFERENCES public.professional(id),
  CONSTRAINT fk_payable_order FOREIGN KEY (order_id) REFERENCES public.order(id)
);
CREATE TABLE public.payment (
  id uuid NOT NULL DEFAULT gen_random_uuid(),
  order_id uuid NOT NULL,
  gateway text NOT NULL,
  gateway_ref text NOT NULL,
  method USER-DEFINED NOT NULL,
  amount_cents integer NOT NULL,
  gateway_fee_cents integer NOT NULL DEFAULT 0,
  platform_fee_cents integer NOT NULL DEFAULT 0,
  status USER-DEFINED NOT NULL,
  paid_at timestamp with time zone,
  created_at timestamp with time zone NOT NULL DEFAULT now(),
  updated_at timestamp with time zone NOT NULL DEFAULT now(),
  CONSTRAINT payment_pkey PRIMARY KEY (id),
  CONSTRAINT payment_order_id_fkey FOREIGN KEY (order_id) REFERENCES public.order(id),
  CONSTRAINT fk_payment_order FOREIGN KEY (order_id) REFERENCES public.order(id)
);
CREATE TABLE public.payoutbatch (
  id uuid NOT NULL DEFAULT gen_random_uuid(),
  period_from timestamp with time zone NOT NULL,
  period_to timestamp with time zone NOT NULL,
  status USER-DEFINED NOT NULL,
  file_url text,
  created_by text NOT NULL,
  created_at timestamp with time zone NOT NULL DEFAULT now(),
  CONSTRAINT payoutbatch_pkey PRIMARY KEY (id)
);
CREATE TABLE public.payoutitem (
  id uuid NOT NULL DEFAULT gen_random_uuid(),
  batch_id uuid NOT NULL,
  professional_id uuid NOT NULL,
  amount_cents integer NOT NULL,
  destination_snap jsonb NOT NULL,
  status USER-DEFINED NOT NULL,
  transfer_ref text,
  error_message text,
  created_at timestamp with time zone NOT NULL DEFAULT now(),
  CONSTRAINT payoutitem_pkey PRIMARY KEY (id),
  CONSTRAINT payoutitem_batch_id_fkey FOREIGN KEY (batch_id) REFERENCES public.payoutbatch(id),
  CONSTRAINT payoutitem_professional_id_fkey FOREIGN KEY (professional_id) REFERENCES public.professional(id),
  CONSTRAINT fk_payoutitem_batch FOREIGN KEY (batch_id) REFERENCES public.payoutbatch(id),
  CONSTRAINT fk_payoutitem_professional FOREIGN KEY (professional_id) REFERENCES public.professional(id)
);
CREATE TABLE public.professional (
  id uuid NOT NULL DEFAULT gen_random_uuid(),
  user_id text NOT NULL,
  display_name text NOT NULL,
  payout_method_id uuid,
  created_at timestamp with time zone NOT NULL DEFAULT now(),
  updated_at timestamp with time zone NOT NULL DEFAULT now(),
  CONSTRAINT professional_pkey PRIMARY KEY (id),
  CONSTRAINT fk_professional_payout_method FOREIGN KEY (payout_method_id) REFERENCES public.professional_payout_method(id)
);
CREATE TABLE public.professional_payout_method (
  id uuid NOT NULL DEFAULT gen_random_uuid(),
  professional_id uuid NOT NULL,
  method text NOT NULL,
  pix_key_type USER-DEFINED,
  pix_key text,
  bank_code text,
  branch text,
  account text,
  account_digit text,
  account_type USER-DEFINED,
  doc_type USER-DEFINED,
  doc_number text,
  holder_name text,
  verified boolean NOT NULL DEFAULT false,
  created_at timestamp with time zone NOT NULL DEFAULT now(),
  CONSTRAINT professional_payout_method_pkey PRIMARY KEY (id),
  CONSTRAINT professional_payout_method_professional_id_fkey FOREIGN KEY (professional_id) REFERENCES public.professional(id),
  CONSTRAINT fk_ppm_professional FOREIGN KEY (professional_id) REFERENCES public.professional(id)
);