-- PostgreSQL superuser ile bir kez çalıştırın; parolayı gerçek ve güçlü bir değerle değiştirin.
CREATE USER karakus_app WITH PASSWORD 'CHANGE_TO_A_STRONG_PASSWORD';
CREATE DATABASE karakus_aluminyum OWNER karakus_app;
