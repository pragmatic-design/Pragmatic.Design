-- One database per service on the one development server. Nothing in a service's connection string
-- names the others'.
CREATE DATABASE warehouse_orders;
CREATE DATABASE warehouse_stock;
CREATE DATABASE warehouse_shipping;
