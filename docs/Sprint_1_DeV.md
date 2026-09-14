## BSc (Hons) in Computer Science Bismarck PAM - Sprint 1 Year 3 Semester 1

Group Number: 14 | Developer for the Sprint 1: IT24101867 - Dissanayake D. M. S. T


## Sprint 1 – Developer Summary Report

## Overview

This sprint, I was responsible for building the platform end to end: the two backend services, the Identity Service and the Resource Service, along with the initial React frontend that brings them together into a working application. The goal was to deliver a secure, well-tested foundation, from the database up to the browser, that future privilege-management features could be built on with confidence.

## Identity Service

I built the Identity Service as a standalone ASP.NET Core Web API with its own dedicated PostgreSQL database, using EF Core Code-First migrations throughout. It provides full CRUD functionality for managing users, including assigning roles and departments, and uses soft- delete so deactivated accounts disappear from listings without being lost from the database. All input is validated with Fluent Validation, covering missing fields, duplicate emails, and the correct HTTP status codes for every scenario.



On top of the CRUD work, I added a full authentication layer: passwords are hashed with BCrypt before they ever touch the database, and a dedicated login endpoint issues a signed JWT on successful sign-in. Every user-management endpoint is now locked behind that token, so only authenticated requests can view or modify user data.



## Resource Service

Following the same structure and conventions as Identity Service, I built the Resource Service to manage the platform's protected resources – tracking each one's type, owner, environment, and criticality level (Low, Medium, High, or Critical). It has its own dedicated database, supports full CRUD with decommission (soft-delete) behaviour, and validates every request before it reaches the database.


I extended the same JWT authentication into this service too, so both services trust tokens signed with the same key. In practice, this means a single login through Identity Service is enough to access either service – neither one needs to know the internal details of the other.


## Testing

I treated automated testing as a core part of the work rather than something to add afterwards. Each service has unit tests covering its business logic and validation rules, plus integration tests that spin up a real PostgreSQL database to exercise every endpoint end-to-end, including authentication, validation failures, and soft-delete behaviour. By the end of the sprint, Identity Service had 27 passing automated tests and Resource Service had 47, comfortably clearing the coverage target set for this sprint.

## Bug Fixes and Refinements

Testing and QA feedback during the sprint surfaced two issues that I investigated and resolved:

- The login endpoint was returning a generic 
invalid credentials
 error for malformed email addresses instead of a clear validation error. I added proper input validation so badly formatted requests are now rejected immediately with a clear message, while genuinely incorrect credentials still return the correct error.


- The user-listing and login endpoints were returning the same information under two different field names, which broke the frontend's user directory screen. I brought both responses in line with each other, so the API is now consistent.


## Frontend

I also built the initial React frontend, giving the platform its first real user-facing screens: a login page that authenticates against the Identity Service, a protected user directory listing every user with their role and department, and a resource list showing each resource’s type, environment, and criticality. All three screens are wired to the live APIs rather than placeholder data, with loading, empty, and error states handled on every screen, and navigation between them built with React Router behind a protected-route guard so only authenticated users can reach them.

On the integration side, I configured cross-origin (CORS) support on both backend services so the frontend could call them directly from the browser, and built the frontend’s API layer to attach the JWT to every request and handle expired or missing tokens gracefully. I then verified the full flow end to end – login, user directory, and resource list – against both services running together, and covered the frontend with its own automated test suite alongside the backend tests.


## Summary

By the end of Sprint 1, the Identity Service, Resource Service, and the initial frontend were all feature-complete against their user stories, fully authenticated, thoroughly tested, and confirmed working together as a single, functioning application. This gives the team a solid, secure base – backend and frontend alike, to build the platform's privilege and approval features on in the sprints ahead.
