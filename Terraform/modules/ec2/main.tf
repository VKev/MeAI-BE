
# Latest Amazon Linux 2 ECS-optimised AMI
data "aws_ami" "ecs_optimized" {
  most_recent = true
  owners      = ["amazon"]

  filter {
    name   = "name"
    values = ["amzn2-ami-ecs-hvm-*-x86_64-ebs"]
  }

  filter {
    name   = "virtualization-type"
    values = ["hvm"]
  }
}

########################
# SECURITY GROUP
########################
resource "aws_security_group" "ec2_sg" {
  name_prefix = "${var.project_name}-ec2-sg"
  vpc_id      = var.vpc_id
  description = "SG for ECS container instances"

  ingress {
    description = "SSH"
    from_port   = 22
    to_port     = 22
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"] # tighten in production
  }

  # dynamic ports for ALB/NLB + tasks running in bridge mode
  ingress {
    description     = "ALB to ECS dynamic ports"
    from_port       = 32768
    to_port         = 65535
    protocol        = "tcp"
    security_groups = [var.alb_security_group_id]
  }

  ingress {
    description = "VPC internal traffic on dynamic ports"
    from_port   = 32768
    to_port     = 65535
    protocol    = "tcp"
    cidr_blocks = [var.vpc_cidr]
  }

  egress {
    description = "All outbound"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = { Name = "${var.project_name}-ec2-sg" }
}

##################
# IAM FOR EC2
##################
resource "aws_iam_role" "ec2_role" {
  name = "${var.project_name}-ec2-role"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Action    = "sts:AssumeRole"
      Effect    = "Allow"
      Principal = { Service = "ec2.amazonaws.com" }
    }]
  })
}

resource "aws_iam_role_policy_attachment" "ecs_instance_policy" {
  role       = aws_iam_role.ec2_role.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonEC2ContainerServiceforEC2Role"
}

resource "aws_iam_role_policy_attachment" "ssm_instance_policy" {
  role       = aws_iam_role.ec2_role.name
  policy_arn = "arn:aws:iam::aws:policy/AmazonSSMManagedInstanceCore"
}

resource "aws_iam_instance_profile" "ec2_profile" {
  name = "${var.project_name}-ec2-profile"
  role = aws_iam_role.ec2_role.name
}

#################################
# OPTIONAL KEY-PAIR
#################################
resource "tls_private_key" "ec2_key" {
  algorithm = "RSA"
  rsa_bits  = 4096
}

resource "aws_key_pair" "ec2_key" {
  key_name   = "${var.project_name}-ec2-key"
  public_key = tls_private_key.ec2_key.public_key_openssh
}

#################################
# ECS CLUSTER
#################################
resource "aws_ecs_cluster" "main" {
  name = "${var.project_name}-cluster"

  setting {
    name  = "containerInsights"
    value = "enabled"
  }

  tags = { Name = "${var.project_name}-cluster" }
}

#################################
# USER-DATA â€“ join the cluster
#################################
locals {
  requested_container_instance_groups = length(var.container_instance_groups) > 0 ? var.container_instance_groups : {
    default = {
      instance_type       = var.instance_type
      root_volume_size    = var.root_volume_size
      associate_public_ip = var.associate_public_ip
      instance_attributes = {}
      tags                = {}
      user_data_extra     = ""
    }
  }

  container_instance_groups = {
    for name, cfg in local.requested_container_instance_groups :
    name => {
      instance_type       = coalesce(lookup(cfg, "instance_type", null), var.instance_type, "t2.micro")
      root_volume_size    = lookup(cfg, "root_volume_size", var.root_volume_size)
      associate_public_ip = coalesce(lookup(cfg, "associate_public_ip", null), var.associate_public_ip, false)
      instance_attributes = lookup(cfg, "instance_attributes", {})
      tags                = lookup(cfg, "tags", {})
      user_data_extra     = join("", compact([lookup(cfg, "user_data_extra", "")]))
    }
  }

  ecs_user_data = {
    for name, cfg in local.container_instance_groups :
    name => templatefile("${path.module}/templates/ecs-user-data.sh.tftpl", {
      cluster_name        = aws_ecs_cluster.main.name
      instance_attributes = jsonencode(cfg.instance_attributes)
      extra_user_data     = cfg.user_data_extra
    })
  }
}

resource "aws_instance" "ecs_instance" {
  for_each = local.container_instance_groups

  ami                         = data.aws_ami.ecs_optimized.id
  instance_type               = each.value.instance_type
  subnet_id                   = var.subnet_id
  vpc_security_group_ids      = [aws_security_group.ec2_sg.id]
  key_name                    = aws_key_pair.ec2_key.key_name
  iam_instance_profile        = aws_iam_instance_profile.ec2_profile.name
  user_data_base64            = base64encode(local.ecs_user_data[each.key])
  monitoring                  = true
  ebs_optimized               = each.value.instance_type != "t2.micro"
  associate_public_ip_address = each.value.associate_public_ip

  root_block_device {
    volume_size           = each.value.root_volume_size
    volume_type           = "gp3"
    delete_on_termination = true
    encrypted             = true
    tags                  = { Name = "${var.project_name}-${each.key}-ec2-root" }
  }

  tags = merge({
    Name             = "${var.project_name}-${each.key}-ecs-instance"
    AmazonECSManaged = "true"
    Cluster          = aws_ecs_cluster.main.name
  }, each.value.tags)

  lifecycle { create_before_destroy = true }
}

resource "aws_eip" "ec2_eip" {
  for_each = { for name, cfg in local.container_instance_groups : name => cfg if cfg.associate_public_ip }

  instance = aws_instance.ecs_instance[each.key].id
  domain   = "vpc"

}
