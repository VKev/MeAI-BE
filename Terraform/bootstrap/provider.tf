terraform {
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.87"
    }
  }

  # Local backend for bootstrap to avoid circular dependency
  backend "local" {}
}

provider "aws" {
  region = var.region
}


