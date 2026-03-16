create database Project_Mouse;
\c test_db_01
\d
drop database test_db_01;

create table player(
    id serial primary key, 
    user_name varchar(32) UNIQUE,
    password varchar(32),
    global_player_info json,
    player_inventory json,
    farm_state json,
    dispatch_state json,
    indoor_state json,
    mail_box json,
    task_state json,
    friend_state json
    );

ALTER TABLE player
ADD COLUMN current_player_state json;
# 建表
create table test(id serial primary key, name varchar(16));

# 插入数据
insert into test(name) values('枫枫');

# 查记录
select * from test;

# 查看当前库下的所有表
\d

# 看表结构
\d test


CREATE SCHEMA schema_name_01;
# 创建表到指定模式
CREATE TABLE schema_name_01.table_name_01 (
    id serial primary key, name varchar(16)
);
# 切换模式
SET search_path TO schema_name_01;
# 查看模式列表
\dn;
# 删除模式
DROP SCHEMA schema_name_01 CASCADE;

ALTER TABLE test
ADD COLUMN testg_jsonb jsonb;

##Project_Mouse

create database Project_Mouse;

create table player(
    id serial primary key, 
    user_name varchar(32) UNIQUE,
    password varchar(32),
    global_player_info json,
    player_inventory json,
    farm_state json,
    dispatch_state json,
    indoor_state json,
    mail_box json,
    task_state json,
    friend_state json
    );

ALTER TABLE player
ADD COLUMN current_player_state json;


create table player_behavior(
    behavior_id serial primary key, 
    user_name varchar(32),
    behavior_time DATE,
    behavior_detial json,
     CONSTRAINT fk_user_name
      FOREIGN KEY(user_name)
        REFERENCES player(user_name)
    );

create table global_game_design_data(
 id serial primary key, 
 data_id varchar(32) UNIQUE,
 detail_info json
) 

ALTER USER postgres WITH PASSWORD 'new_password_223';