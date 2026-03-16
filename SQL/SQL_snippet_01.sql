-- Table: public.player

-- DROP TABLE IF EXISTS public.player;

CREATE TABLE IF NOT EXISTS public.player
(
    id SERIAL PRIMARY KEY,
    user_name character varying(32) COLLATE pg_catalog."default",
    password character varying(32) COLLATE pg_catalog."default",
    global_player_info json,
    
    player_inventory json,
 
    planting_state json,
    dispatch_state json,
    bed_room_state json,
    living_room_state json,
    toilet_room_state json,
    balcony_room_state json,
    weather_info json,


    mail_box json,
    task_state json,
    friend_state json,
 
    
    CONSTRAINT player_pkey PRIMARY KEY (id),
    CONSTRAINT player_user_name_key UNIQUE (user_name)
)

TABLESPACE pg_default;

ALTER TABLE IF EXISTS public.player
    OWNER to postgres;


ALTER TABLE IF EXISTS public.player
    OWNER to postgres;


    DROP TABLE IF EXISTS player_network_state;
    ALTER TABLE IF EXISTS public.player_network_state
    OWNER to postgres;

    CREATE UNLOGGED TABLE  player_network_state (
     id SERIAL PRIMARY KEY,
    user_name character varying(32) COLLATE pg_catalog."default" NOT NULL,
    network_state character varying(64),
    server_id character varying(64),
    CONSTRAINT player_network_state_user_name_fkey FOREIGN KEY (user_name)
        REFERENCES public.player (user_name),
    CONSTRAINT player_network_state_user_name_key UNIQUE (user_name),

      CONSTRAINT player_network_state_server_id_key UNIQUE (server_id)
);


